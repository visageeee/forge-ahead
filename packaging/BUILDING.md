# Weland SE desktop packages

`.github/workflows/desktop-builds.yml` tests the editor geometry and builds Windows
x64, Linux x64, macOS Apple Silicon, and macOS Intel packages. It runs on pushes and pull
requests to `weland-gtk3`, tags beginning with `v`, and manual dispatches.

## Download a development build

Push this change to your fork, then open **Actions → Build Weland SE desktops**.
Select the completed run and download the appropriate artifact. GitHub wraps
artifacts in an extra ZIP: extract it, then extract the Weland archive inside.

Windows: open `Weland-SE-win-x64/Weland.exe`. Keep the complete folder together.
Mac: drag `Weland SE.app` into Applications. The first packages are ad-hoc signed,
not Apple-notarized; macOS may require approval in System Settings → Privacy &
Security after the first launch attempt. Do not disable Gatekeeper globally.
Windows and Mac packages include .NET 8 and GTK 3; no separate runtime
installation is needed.

Linux: extract `Weland-SE-linux-x64.tar.gz` and run `Start-Weland-SE.sh`.
The Linux package includes .NET 8 and uses system GTK 3. On Ubuntu 24.04:

```sh
sudo apt install libgtk-3-0t64 librsvg2-common adwaita-icon-theme
```

It targets Ubuntu 24.04 or compatible newer x64 distributions, rather than all
Linux distributions. It does not require installing the .NET SDK.

## Release builds

After a development build has been tested, push a version tag, for example:

```sh
git tag v0.1.0
git push origin v0.1.0
```

Successful tagged builds create a GitHub Release in the same repository and attach
all four archives. This workflow never pushes commits to another repository.

## Building locally

Use the .NET 8 SDK. Windows packaging also requires an MSYS2 UCRT64 installation
with `mingw-w64-ucrt-x86_64-gtk3`, `mingw-w64-ucrt-x86_64-librsvg`, and
`mingw-w64-ucrt-x86_64-adwaita-icon-theme`. In PowerShell:

```powershell
$env:MSYS2_ROOT = 'C:\msys64'
./packaging/windows.ps1
```

On a Mac, install Homebrew GTK and package for that Mac's architecture:

```sh
brew install gtk+3 librsvg adwaita-icon-theme
python3 packaging/macos.py osx-arm64
# On Intel instead: python3 packaging/macos.py osx-x64
```

On Linux with the .NET 8 SDK, run:

```sh
bash packaging/linux.sh
```

Output is in `dist/`. Mac packaging recursively copies native dependencies,
rewrites their load commands, preserves runtime resources and license notices,
and signs the app. Image loader caches are regenerated for the extraction path.
Windows native libraries sit alongside the executable; startup configures its
resource paths before loading GtkSharp. Ordinary Linux development is unchanged.

## Checks before publishing

A successful build does not prove the GUI works. Test each package on a machine
without a separately installed .NET runtime (or GTK for Windows/Mac), including installation paths
with spaces. Check launch, box/circle/line editing, undo, map save/reopen, terminal
PNG/JPEG import and preview, and launching/returning from visual mode with that
platform's Aleph One/Sprintathon executable.

Apple notarization and a trusted Windows publisher signature are not configured.
The build currently uses .NET 8; plan a framework upgrade before its support ends.
GTK packages are resolved from current Homebrew/MSYS2 repositories, so rebuilding
later may change native dependency versions. CI logs record installed packages.
