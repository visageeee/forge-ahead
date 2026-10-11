#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
out="$PWD/dist/Weland-SE-linux-x64"
rm -rf "$out"
mkdir -p "$out"
dotnet publish Weland.csproj -c Release -r linux-x64 --self-contained true \
    -p:PublishTrimmed=false -o "$out"
cp COPYING.txt packaging/BUILDING.md "$out/"
cp icons/Weland_256x256.png "$out/weland-se.png"
cat > "$out/Start-Weland-SE.sh" <<'LAUNCHER'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
exec "$here/Weland" "$@"
LAUNCHER
chmod +x "$out/Weland" "$out/Start-Weland-SE.sh"
cat > "$out/README-Linux.txt" <<'README'
Weland SE — Linux x64

Extract the whole archive and run ./Start-Weland-SE.sh or ./Weland.
The .NET runtime is included. GTK 3 and its image loaders are system dependencies.
This package is built on Ubuntu 24.04 and targets Ubuntu 24.04 or compatible newer
x64 distributions. It is not an AppImage or a fully bundled GTK runtime.

On Ubuntu 24.04, install the native dependencies with:
  sudo apt install libgtk-3-0t64 librsvg2-common adwaita-icon-theme

Keep the folder's files together. Aleph One/Sprintathon is installed separately
and can be selected in Weland preferences for visual mode.
README
tar -czf "$PWD/dist/Weland-SE-linux-x64.tar.gz" -C "$PWD/dist" Weland-SE-linux-x64
