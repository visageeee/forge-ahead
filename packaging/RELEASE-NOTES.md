Weland SE is an expanded Weland map editor for Marathon, Aleph One, and Sprintathon.

Features include snapping box and circle tools, polygon insets and line cuts,
group selection, move/rotate/scale tools, configurable hover highlights, undo
history, embedded Lua, fog/collection MML export, and a terminal editor with
picture import and preview.

### Downloads

- **Windows x64:** extract `Weland-SE-win-x64.zip` and open `Weland.exe` inside its folder.
- **Apple Silicon Mac:** extract `Weland-SE-osx-arm64.zip` and move `Weland SE.app` to Applications.
- **Intel Mac:** use `Weland-SE-osx-x64.zip` instead.
- **Linux x64:** extract `Weland-SE-linux-x64.tar.gz` and run `Start-Weland-SE.sh`.

.NET is included in every package. Windows and Mac also bundle GTK.
Linux targets Ubuntu 24.04 or compatible newer x64 distributions and needs
system GTK 3 and SVG loaders. On Ubuntu 24.04:

```sh
sudo apt install libgtk-3-0t64 librsvg2-common adwaita-icon-theme
```

Mac packages are ad-hoc signed, without Apple notarization. macOS may require
approval in System Settings → Privacy & Security after the first launch attempt.
Windows packages do not yet have a trusted publisher signature.

Scenario Shapes files and Aleph One/Sprintathon for visual mode are installed
separately. Choose their paths in Weland preferences. Keep each package's files
together. Please report platform issues with the package name and an error log.
