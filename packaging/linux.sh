#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
out="$PWD/dist/Forge-Ahead-linux-x64"
rm -rf "$out"
mkdir -p "$out"
dotnet publish Weland.csproj -c Release -r linux-x64 --self-contained true \
    -p:PublishTrimmed=false -o "$out"
cp COPYING.txt packaging/BUILDING.md "$out/"
cp icons/Weland_256x256.png "$out/forge-ahead.png"
cat > "$out/Start-Forge-Ahead.sh" <<'LAUNCHER'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
exec "$here/ForgeAhead" "$@"
LAUNCHER
chmod +x "$out/ForgeAhead" "$out/Start-Forge-Ahead.sh"
cat > "$out/README-Linux.txt" <<'README'
Forge Ahead — Linux x64

Extract the whole archive and run ./Start-Forge-Ahead.sh or ./ForgeAhead.
The .NET runtime is included. GTK 3 and its image loaders are system dependencies.
This package is built on Ubuntu 24.04 and targets Ubuntu 24.04 or compatible newer
x64 distributions. It is not an AppImage or a fully bundled GTK runtime.

On Ubuntu 24.04, install the native dependencies with:
  sudo apt install libgtk-3-0t64 librsvg2-common adwaita-icon-theme

Keep the folder's files together. Aleph One/Sprintathon is installed separately
and can be selected in Forge Ahead preferences for visual mode.
README
tar -czf "$PWD/dist/Forge-Ahead-linux-x64.tar.gz" -C "$PWD/dist" Forge-Ahead-linux-x64
