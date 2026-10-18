# Forge Ahead

**Map editor for Marathon**

Forge Ahead is an expanded fork of [Weland](https://github.com/treellama/weland),
the Marathon map editor originally created by Gregory Smith. It supports map
editing for the original Marathon games, the Aleph One source port and it's forks, using .NET 8 and GTK 3.

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
  octagons with square fillers, or Penrose thin and thick rhombi, using a chosen
  edge length.
- **Ring tool:** create circular or elliptical rings with up to 128 outer sides
  and multiple connected inner rings. Rings are built from convex sectors so
  individual polygons remain within the engine's eight-vertex limit.
- **Stairs:** add stepped floor heights to rings and tessellated patterns. Ring
  heights progress clockwise; ceilings can stay fixed or follow the floor.
- **Line cuts:** attach new vertices to existing lines and split polygons with
  the Line tool. An optional split-all mode cuts every crossed polygon, creates
  shared intersection vertices, and connects the path through empty gaps.

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
- **Polygon Lua triggers:** assign entry code from the Select sidebar, with
  optional once-per-level activation and automatic embedding. Categorized
  action presets cover ammo, healing, sounds, monsters, lights, doors and more.
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

### Experimental baked floor lighting

With the Select tool, select the receiving polygons, then choose the **Bake lighting**
tool (sun icon). Click and drag inside a polygon to place or move the light with a live shadow preview. Its dedicated
sidebar contains the preview and bake controls. Set
its **absolute height** in world units (above the floor and below the ceiling), the
lit and shadow brightness percentages, and drag the light (or click **Preview shadows**). Yellow regions
are lit; dark regions are shadowed. This display-only overlay does not generate
geometry, create lights, or depend on the bake budget and shared-edge limits.
**Bake floor lighting** then attempts to build matching map geometry; baked shadows
may be coarser than the live preview.

This creates ordinary convex map polygons and two constant floor lights. Raised
floors cast finite shadows according to the source height. Selected floors at or
above the light receive the shadow brightness: their top surfaces cannot receive
direct light from below. They still block light reaching lower floors. The source
only needs to be between the floor and ceiling of its own containing polygon. One-sided map boundaries
block light. All map geometry can cast shadows, but only selected floors are changed.
Original floor textures, heights and other surface settings are retained. There is
no distance falloff, soft shadow or dynamic platform lighting. Floor shadow geometry
currently uses raised-floor blockers; ceiling and wall sampling tests both floor
and ceiling openings.
Select normal polygons without Lua triggers. The polygon budget limits output pieces;
the minimum area is in squared map-coordinate units (1024 coordinate units = 1 WU).
If exact shadow intersections cannot be built, the editor automatically retries with
approximate shadows on an integer-coordinate subdivision. Shadow edges may be angular
and very narrow shadows may be missed, but floor coverage is preserved. The bake result message identifies approximate results. Refinement uses up to 64 pieces per selected floor,
within the total polygon budget. Shared-edge vertex limits trigger retries at lower
detail. If necessary, each selected floor receives a single light level sampled at
its center, with no new geometry; the bake result message reports this fallback. Unsafe geometry still cancels without changing the
map; increase the budget or use a smaller selection if necessary.

Preview is temporary. Undo reverses a complete bake, including added lights and
geometry; redo reapplies it. Repeated bakes in the same editing session automatically reuse the pre-bake geometry
while the map and scope are otherwise unchanged. After reopening a baked map or making
other map edits, use Undo or an unbaked copy if you want to discard the earlier splits. Light markers are editor-only and are not saved. Existing
polygon IDs are retained for one resulting piece; references to that polygon do not
automatically expand to include the other pieces. Back up maps before experimenting.

Wall lighting is included when baking: existing sides belonging to selected polygons
(and their new pieces) receive lit or shadow brightness. Fifteen rays across the width and height of each
primary, secondary and transparent wall section test intervening floor/ceiling openings
and solid boundaries. Back-facing wall surfaces are shadowed. Textures, offsets and
control-panel settings are preserved; unselected wall sides are unchanged. This is
one light value per existing wall section, blended between lit and shadow brightness
according to the blocked sample fraction, not a vertically subdivided shadow.
Intermediate constant lights are shared across walls within the bake. The
live top-down overlay still previews floor shadows only; inspect walls in visual mode.

Bake results appear directly under the Bake button and in the status bar, including
failure reasons and the number of new polygons. Failed geometry attempts retry at
progressively lower detail for all geometry errors, including budget and precision
failures. A final lighting-only fallback creates no shadow boundaries and is explicitly
reported as **Simplified**, with a plain-language fallback reason. Each retry
starts from the unchanged map. The budget must cover at least the selected polygon
count; invalid source placement and unsupported polygon types still require correction.

**Whole map** is enabled by default in the lighting sidebar. Preview and Bake use all
map polygons without requiring a selection; uncheck it to work on selected polygons.
For whole-map baking the budget is raised to at least the existing polygon count.
Special polygons (including platforms) and polygons with Lua triggers receive lighting
without being split, preserving their IDs and behavior. Their floor brightness is
sampled at the center. Platform shadows remain static at their current map height.

Successful approximate bakes report the approximation as applied, rather than repeating
the internal exact-intersection error. “Shadow edges required coordinate rounding”
is a fallback explanation, not a failed bake. “Simplified” explicitly means that no
new shadow boundaries were created. Actual failures remain prefixed “Bake failed”.

Lighting bakes simplify their generated pieces before creating map geometry. Adjacent
pieces with the same shade are merged repeatedly when their union is convex and fits
the eight-vertex limit. Unused sampling corners are discarded; original map vertices
and junctions needed by other pieces remain. Merging stays inside each original floor,
so its height, texture and other polygon settings are preserved. This is a greedy
simplification, not a guarantee of the smallest possible polygon count.

### Rebuild lighting geometry

Enable **Rebuild lighting geometry** in the lighting sidebar to reconstruct connected
compatible floor regions. Region outlines and holes are extracted without the
eight-vertex limit, old internal edges and unused interior vertices are discarded,
and lit and shadowed pieces are installed together across each region.
Only adjacent normal polygons with matching surface settings can merge; height,
texture, light, media and other setting changes stay intact. Shared control panels
and transparent wall textures prevent a merge. Convexity and eight-vertex limits
apply to the final pieces. Concave outlines and holes are supported; the result is
not guaranteed to have the mathematically smallest polygon count.

The operation and its lighting changes form one undo step. Objects are reassigned
by the existing Pathfinder geometry operation. Polygon IDs may change. If embedded
Lua, polygon Lua triggers or terminal data are present, rebuilding is conservatively
skipped and the normal bake runs; the result message reports this. External scripts
cannot be detected: leave this mode off if external code depends on polygon IDs.
Preview continues to show the intended
lighting, not the final rebuilt polygon mesh. The result reports removed boundaries.

Bake diagnostics report the underlying geometry error instead of a generic safety
fallback. Where available they include the polygon ID and construction stage, plus
the last rejected approximation. The result label is selectable so it can be copied
with a bug report. With rebuilding enabled, IDs refer to the temporary merged map.

Lighting normalizes imported polygon edge ordering on its working copy before
splitting or rebuilding. Maps whose vertices are paired with outgoing edges are
converted to the editor's incoming-edge convention, carrying side and adjacency
entries with their lines. This changes no coordinates or winding and is included
in the bake's undo step. It prevents false shared-edge conflicts when mixing imported
polygons with newly generated shadow polygons.

Boundary-based lighting allows a bounded integer-rounding area difference (at most
one coordinate unit times the receiving floor perimeter), then still validates the
constructed geometry for crossings, overlaps and shared-edge ownership. This avoids
rejecting otherwise valid shadow boundaries for sub-unit intersection rounding.
With **Rebuild lighting geometry**, **Allow coarse approximation** is off by default:
if boundary-based construction fails, the bake leaves the map unchanged and reports
why. Enable it explicitly to permit the sampling-triangle fallback. Moving the sun and baking again reuses the unbaked source while the last bake is
otherwise unchanged.

Automatic rebaking stores the original map and the last baked result in memory.
It recomputes both lit and shadowed regions from that original, replacing old shadow
splits and lights rather than layering another bake onto them. The result message
confirms replacement. Each replacement is undoable back to the preceding baked result.
Any intervening map change, different selected scope, or different map prevents reuse,
so edits are never overwritten. The source snapshot is not saved in the map file.
This fixes accumulated geometry; it does not remove integer-coordinate rounding or
make the optional sampling approximation exact.

The pre-bake rebuild ignores old **floor and ceiling light IDs** when checking compatibility:
these will be replaced by the new bake. Thus subdivisions left solely by previous
floor shading can be removed even after reopening a map. The post-bake merge still
requires matching floor lights so new shadow boundaries survive. Ceiling settings,
textures, media, sounds, special behavior and protected shared sides remain meaningful
boundaries. Reconstruction spans the connected compatible region rather than
requiring each original pair to form a convex polygon.

Region reconstruction rebuilds boundary-side records and reassigns objects and notes
by position. Solid/decorative/landscape shared lines, control panels and transparent
wall textures remain protected even when another route connects their polygons.
Groups containing built-in polygon destinations keep their geometry. Reconstruction
is performed on a disposable working map and only committed after topology validation.
If a region cannot be reconstructed safely, the current map is left unchanged.

When baking a reconstructed region, the already clipped shadow pieces are used
directly. Regions with multiple raised islands do not require a second, unused
hole-bridging triangulation. Boundary area, planned coverage and final topology
are still checked before applying the bake.

Lighting simplification also tries exact multi-polygon convex unions when pairwise
merging stalls. This removes unnecessary flat subdivisions such as pinwheel
layouts while retaining shade boundaries and the eight-vertex limit. It is a
simplification pass, not a guarantee of the minimum possible polygon count.

After merging, lighting-region rebuilding now cancels internal same-shade edges,
traces the remaining boundary and repartitions it from scratch. This can replace
old diagonals in newly lit concave areas instead of merely joining old polygons.
Holes remain excluded. A new partition is used only when it has fewer polygons,
exactly preserves area and meets shared-vertex limits; ambiguous boundaries or
failed triangulation retain the valid merged layout. Protected region boundaries
are unchanged.

Lighting reconstruction retains textured wall-segment endpoints and restores
sides according to the recreated line direction. This preserves distinct wall
textures and their horizontal offsets while removing internal floor divisions.

Ceilings are shaded automatically when baking. Area-weighted samples across each
receiving ceiling test visibility through intervening floor/ceiling openings and
one-sided walls. Each ceiling receives one blended brightness using the same lit
and shadow settings as the floor; ceiling shading does not add polygon splits.
The live preview still shows floors only. Ceiling textures are preserved, and
ceiling light changes participate in save/load and bake undo/redo.

Lighting reconstruction joins generated seams that differ by at most one map
coordinate and removes nearly collinear generated corners. Original surviving
map vertices remain protected. This avoids thin fragments caused by independently
rounded shadow intersections; final area and topology checks still apply.
Use **Rebuild lighting geometry** for this pass. Leave **Allow coarse approximation**
off to avoid replacing a failed detailed bake with sampling triangles.

Reconstruction snapshots wall sides and flags before deleting any regions and
restores them after all regions have been built. New internal shadow boundaries
remain open portals; intentional original wall flags are retained. Wall matching
allows one-coordinate rounding at generated splits, and side types are updated
from the final adjacent floor and ceiling heights.

### Sun Lua presets (Sprintathon)

The **Sun** category uses Sprintathon's eight-point `Level.sun.path` API.
**Declare eight-step sun path** provides azimuth and elevation for each of its
exactly eight points. Insert it on an enabled polygon trigger; the editor emits
its declaration at the top of the generated level Lua, before callbacks and
lighting precomputation. It does not redeclare the path when entering a polygon.
Only one path is supported per level: identical declarations are deduplicated;
conflicting declarations report an error.

**Smooth sunrise** runs from path position 0 to 1; **Smooth sunset** runs from 1
to 0. Duration, start/end positions and brightness are editable. Colors change
smoothly between warm dawn/dusk and daylight. **Set sun instantly** and
**Transition sun smoothly** also use a path position, plus size, RGB color,
brightness, shafts, glare and bloom. Enable, disable, stop and reset remain
available; reset clears appearance overrides but cannot remove a locked path.

When a motion action has no explicit path preset, the generated script supplies
eight points from elevation -3 to 35 degrees at azimuth 250, unless another
level script has already declared a path. Levels without a path/motion preset
receive no automatic declaration. Replace previously inserted sun actions with
these updated presets, Apply, and save to regenerate the level Lua.

Transitions continue after leaving the polygon and restore on saved-game load.
Use Once per level to prevent re-entry from restarting the animation. Sprintathon
uses the declared path for precomputed lighting blends; editor-baked polygon
geometry is unchanged. Visible Sun and Landscape Light Shafts still respect the
player's graphics settings. In-game visual validation remains necessary.
