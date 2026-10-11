#!/usr/bin/env python3
"""Build a self-contained .app using the current architecture's Homebrew GTK."""
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import subprocess
import sys


def run(*args):
    return subprocess.check_output(args, text=True).strip()


def main(rid):
    expected = {'osx-arm64': 'arm64', 'osx-x64': 'x86_64'}
    if rid not in expected or platform.machine() != expected[rid]:
        raise SystemExit('Build on a Mac matching the requested architecture')
    root = Path(__file__).resolve().parent.parent
    os.chdir(root)
    app = root / 'dist' / rid / 'Weland SE.app'
    if app.exists():
        shutil.rmtree(app)
    contents = app / 'Contents'
    native = contents / 'Resources' / 'gtk'
    lib = native / 'lib'
    managed = contents / 'MacOS' / 'app'
    lib.mkdir(parents=True)
    managed.mkdir(parents=True)
    subprocess.run(['dotnet', 'publish', 'Weland.csproj', '-c', 'Release', '-r', rid,
                    '--self-contained', 'true', '-p:PublishTrimmed=false', '-o', str(managed)], check=True)
    formulas = ['gtk+3', 'librsvg', 'adwaita-icon-theme']
    deps = set(formulas)
    for formula in formulas:
        deps.update(run('brew', 'deps', '--installed', formula).splitlines())
    prefixes = [Path(run('brew', '--prefix', f)) for f in sorted(deps)]
    # Runtime resources and license notices remain outside the signed executable folder.
    for prefix in prefixes:
        for name in ('share', 'etc'):
            if (prefix / name).is_dir():
                shutil.copytree(prefix / name, native / name, dirs_exist_ok=True, symlinks=False)
    copied = {}  # canonical source -> flat relocatable destination
    names = {}

    def copy_binary(source):
        source = source.resolve()
        if source in copied:
            return copied[source]
        dest = lib / source.name
        if dest.name in names and names[dest.name] != source:
            raise RuntimeError(f'Conflicting native library name: {source}')
        names[dest.name] = source
        copied[source] = dest
        shutil.copy2(source, dest)
        dest.chmod(0o755)
        # Strip bottle signatures before rewriting load commands.
        subprocess.run(['codesign', '--remove-signature', str(dest)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        paths = []
        lines = run('otool', '-l', str(source)).splitlines()
        for i, line in enumerate(lines):
            if line.strip() == 'cmd LC_RPATH':
                paths.append(lines[i + 2].strip().split(' (offset')[0].removeprefix('path '))
        changes = []
        for line in run('otool', '-L', str(source)).splitlines()[1:]:
            dependency = line.strip().split(' (compatibility')[0]
            if dependency.startswith(('/System/', '/usr/lib/')):
                continue
            if dependency.startswith('@loader_path/'):
                resolved = source.parent / dependency.removeprefix('@loader_path/')
            elif dependency.startswith('@rpath/'):
                tail = dependency.removeprefix('@rpath/')
                candidates = [Path(p.replace('@loader_path', str(source.parent))) / tail for p in paths]
                candidates += [p / 'lib' / tail for p in prefixes]
                resolved = next((p for p in candidates if p.exists()), None)
                if resolved is None:
                    raise RuntimeError(f'Cannot resolve {dependency} in {source}')
            elif dependency.startswith('/'):
                resolved = Path(dependency)
            else:
                raise RuntimeError(f'Unsupported dependency {dependency} in {source}')
            target = copy_binary(resolved)
            changes += ['-change', dependency, '@loader_path/' + target.name]
        subprocess.run(['install_name_tool', '-id', '@loader_path/' + dest.name, *changes, str(dest)], check=True)
        return dest

    # Names GtkSharp 3.24 requests through dlopen (some are unversioned aliases).
    libraries = {
        'libglib-2.0.0.dylib': 'glib', 'libgobject-2.0.0.dylib': 'glib',
        'libgio-2.0.0.dylib': 'glib', 'libcairo.2.dylib': 'cairo',
        'libatk-1.0.0.dylib': 'atk', 'libpango-1.0.0.dylib': 'pango',
        'libpangocairo-1.0.0.dylib': 'pango', 'libgdk-3.0.dylib': 'gtk+3',
        'libgtk-3.0.dylib': 'gtk+3', 'libgdk_pixbuf-2.0.dylib': 'gdk-pixbuf',
    }
    for name, formula in libraries.items():
        prefix = Path(run('brew', '--prefix', formula))
        source = prefix / 'lib' / name
        if not source.exists():
            stem = name.removesuffix('.dylib')
            candidates = list((prefix / 'lib').glob(stem + '.*.dylib'))
            if not candidates:
                raise RuntimeError(f'Missing GtkSharp library {name}')
            source = candidates[0]
        target = copy_binary(source)
        if name != target.name:
            (lib / name).symlink_to(target.name)
    pixbuf = Path(run('brew', '--prefix', 'gdk-pixbuf'))
    query = pixbuf / 'bin' / 'gdk-pixbuf-query-loaders'
    modules = sorted({str(m.resolve()) for prefix in prefixes
                      for m in (prefix / 'lib' / 'gdk-pixbuf-2.0').glob('*/loaders/*')
                      if m.is_file() and m.suffix in ('.so', '.dylib')})
    cache = run(str(query), *modules)
    # Include all discovered loaders (not just JPEG/PNG; Weland uses SVG icons too).
    for source_name in re.findall(r'^"([^"]+)"$', cache, re.MULTILINE):
        source = Path(source_name)
        target = copy_binary(source)
        cache = cache.replace(source_name, '@GTKROOT@/lib/' + target.name)
    if re.search(r'^"/(?:opt|usr/local)/', cache, re.MULTILINE):
        raise RuntimeError('Image cache retains Homebrew paths')
    (native / 'loaders.cache.in').write_text(cache + '\n')
    for prefix in prefixes:
        for module in (prefix / 'lib' / 'gio' / 'modules').glob('*.so'):
            copy_binary(module)
    for dest in copied.values():
        subprocess.run(['codesign', '--force', '--sign', '-', str(dest)], check=True)
        if '/opt/homebrew/' in run('otool', '-L', str(dest)) or '/usr/local/' in run('otool', '-L', str(dest)):
            raise RuntimeError(f'Unrelocated dependency in {dest}')
    shutil.copy2(root / 'COPYING.txt', contents / 'Resources' / 'COPYING.txt')
    shutil.copy2(root / 'packaging' / 'BUILDING.md', contents / 'Resources' / 'BUILDING.md')
    icon = root / 'icons' / 'Weland.icns'
    if icon.exists():
        shutil.copy2(icon, contents / 'Resources' / icon.name)
    info = dict(CFBundleName='Weland SE', CFBundleDisplayName='Weland SE',
                CFBundleIdentifier='org.weland.se', CFBundleExecutable='Weland-SE',
                CFBundlePackageType='APPL', CFBundleVersion='1', CFBundleShortVersionString='1.0',
                NSHighResolutionCapable=True)
    if icon.exists():
        info['CFBundleIconFile'] = icon.name
    with (contents / 'Info.plist').open('wb') as f:
        plistlib.dump(info, f)
    launcher = contents / 'MacOS' / 'Weland-SE'
    launcher.write_text('''#!/bin/bash
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
GTKROOT="$HERE/../Resources/gtk"
export DYLD_LIBRARY_PATH="$GTKROOT/lib"
export GTK_DATA_PREFIX="$GTKROOT"
export GTK_EXE_PREFIX="$GTKROOT"
export FONTCONFIG_PATH="$GTKROOT/etc/fonts"
export XDG_DATA_DIRS="$GTKROOT/share"
export GSETTINGS_SCHEMA_DIR="$GTKROOT/share/glib-2.0/schemas"
export GIO_EXTRA_MODULES="$GTKROOT/lib"
export GDK_BACKEND=quartz
CACHE="$(mktemp -d "${TMPDIR:-/tmp}/weland-gtk.XXXXXX")"
trap 'rm -rf "$CACHE"' EXIT
# Escape replacement metacharacters in user-controlled installation paths.
ESCAPED="$(printf '%s' "$GTKROOT" | sed 's/[\\&|]/\\\\&/g')"
sed "s|@GTKROOT@|$ESCAPED|g" "$GTKROOT/loaders.cache.in" > "$CACHE/loaders.cache"
export GDK_PIXBUF_MODULE_FILE="$CACHE/loaders.cache"
"$HERE/app/Weland" "$@"
''')
    launcher.chmod(0o755)
    # Ad-hoc signing permits local use; public notarization requires an Apple identity.
    subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(app)], check=True)
    subprocess.run(['codesign', '--verify', '--deep', '--strict', str(app)], check=True)
    archive = root / 'dist' / f'Weland-SE-{rid}.zip'
    if archive.exists():
        archive.unlink()
    subprocess.run(['ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', str(app), str(archive)], check=True)
    print(archive)


if __name__ == '__main__':
    main(sys.argv[1])
