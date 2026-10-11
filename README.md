# Forge Ahead

**Map editor for Marathon**

Forge Ahead is an expanded fork of [Weland](https://github.com/treellama/weland),
the Marathon map editor originally created by Gregory Smith. It supports map
editing for Marathon, Aleph One, and Sprintathon, using .NET 8 and GTK 3.

This fork was renamed by request to differentiate it from the original Weland project.

## Development with AI

The enhancements in this fork are developed by using AI coding tools. Feature direction and testing are guided by the repo creator.

### Notable Added features

### Drawing and geometry

- **Box tool:** draw snapped rectangular polygons, connect to existing geometry,
  and create insets at interiors, edges, and corners. Intersections can split
  existing convex polygons while reusing shared vertices and lines.
- **Circle tool:** draw many-sided polygons with a configurable side count,
  including insets into existing polygons.
- **Tessellation:** drag out connected patterns of triangles, squares, hexagons,
  or octagons with square fillers, using a chosen edge length.
- **Ring tool:** create circular or elliptical rings with up to 128 outer sides
  and multiple connected inner rings. Rings are built from convex sectors so
  individual polygons remain within the engine's eight-vertex limit.
- **Stairs:** add stepped floor heights to rings and tessellated patterns. Ring
  heights progress clockwise; ceilings can stay fixed or follow the floor.
- **Line cuts:** attach new vertices to existing lines and split polygons with
  the Line tool.

### Selection and editing

- Drag selection boxes and Shift-click to select multiple geometry items,
  including polygons.
- **Copy and paste** selected geometry, with a placement preview. Polygon copies
  include settings, textures, contained objects, and annotations.
- **Move, rotate, and scale** selections. Scaling supports independent axes and
  warping; hold Shift for constrained scaling. Pan is a separate tool.
- Right-click and drag to move the selection.
- Mirror horizontally, flip vertically, or shift selected floor and/or ceiling
  heights from the Select sidebar.
- **Undo/redo history** with up to 100 actions and a clickable history panel.
- Right-click eyedroppers for heights, liquids, sounds, textures, and polygon types.
- Configurable line width, vertex size, and separate hover highlight colors for
  vertices, lines, and polygons.

### Scenarios, terminals, and scripts

- Open, switch between, edit, and save multiple maps inside an already **merged scenario**.
- **Visual terminal editor** with scalable previews, page editing, and picture
  import while preserving image proportions.
- **Embed Lua scripts** in individual levels, with scripts preserved during
  visual-mode round trips.
- Export **MML** for extra texture/scenery collections and custom fog settings.
- Repair wall-surface references with **Clean Up Wall Surfaces**. **Pave** fills
  missing textures while preserving already textured surfaces.
- Improvements to interactive editing performance and visual-mode return handling.

See [the tool guide](BOX-TOOL.md) for options, geometry limits, and detailed behavior.
Copy/paste currently uses an internal clipboard for the active level.

## Downloads

Get platform packages from [GitHub Releases](https://github.com/visageeee/forge-ahead/releases/latest).

| Platform | Launch |
| --- | --- |
| Windows x64 | Extract the complete ZIP and open `ForgeAhead.exe`. |
| macOS Apple Silicon or Intel | Extract the matching ZIP and move `Forge Ahead.app` to Applications. |
| Linux x64 | Extract the archive and run `Start-Forge-Ahead.sh`. |

Packages include the .NET runtime. Windows and macOS also include GTK; Linux
uses system GTK 3. On Ubuntu 24.04, install the native dependencies with:

```sh
sudo apt install libgtk-3-0t64 librsvg2-common adwaita-icon-theme
```

Keep each package's files together. macOS packages are ad-hoc signed rather than
Apple-notarized; see [packaging instructions](packaging/BUILDING.md) for platform
requirements and first-launch details.

Scenario assets, including Shapes and Sounds, are installed separately.
Aleph One or Sprintathon is also installed separately for visual mode.

## Run from source

Install the **.NET 8 SDK** and GTK 3, then:

```sh
git clone --branch weland-gtk3 https://github.com/visageeee/forge-ahead.git
cd forge-ahead
dotnet run --project Weland.csproj
```

The source project and C# namespace retain the Weland name. Published binaries
use the ForgeAhead name. Existing preferences and plugin folders are preserved;
plugins referencing the old assembly must be rebuilt against ForgeAhead.

Run the automated geometry and serialization checks with:

```sh
dotnet run --project tests/BoxToolTests.csproj -c Release
```

For local packaging and GitHub Actions builds, see
[packaging/BUILDING.md](packaging/BUILDING.md).

## Visual mode

In Preferences, choose your Shapes file, scenario, and Aleph One or Sprintathon
executable. Configure the engine's visual-mode script or plugin, then use Visual
Mode from the View menu. Quit the engine to return the edited map to Forge Ahead.
Terminal editing, Lua embedding, and MML export are available in the editor;
3D texturing uses the external engine.

## Credits and license

**Enhancements by visage**, developed with AI assistance.

**Original Weland app by:** Gregory Smith. Original contributors include
mijienke (custom grid), effigy (tooltips), and Eric Peterson (Smithy).

Application icon by Robert Kreps. Tool artwork includes work from
tango-art-libre, GIMP, and openclipart.org.

Forge Ahead is distributed under the **GNU General Public License, version 2**.
See [COPYING.txt](COPYING.txt) for the license.
