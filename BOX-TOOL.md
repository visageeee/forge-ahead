# Box Polygon Tool

On the `weland-gtk3` branch, use the .NET 8 SDK to build and launch:

```sh
dotnet run --project Weland.csproj
```

Select **Draw Box Polygon** in the toolbar, or press **B**, then drag between
opposite corners. Release to create a filled, axis-aligned rectangular polygon.
The tool uses the existing grid and vertex snapping distance, reuses shared
vertices and walls, and splits partial shared walls without duplicate geometry.
New polygons inherit floor/ceiling settings from their neighbor.

The preview changes color when placement is blocked; the status bar explains
why. Crossing or enclosing existing geometry, partial overlaps, and splits
exceeding Marathon's eight-vertex polygon limit are rejected. These
checks include geometry hidden by view filters. Press **Escape** to cancel.
Undo/redo includes the entire box and any shared-wall splits.

Run the geometry and editor regression tests without GTK:

```sh
dotnet run --project tests/BoxToolTests.csproj
```

# Selection Boxes

Choose Select (**A**) and drag on empty map space to select enclosed vertices
and lines. A line is included only when both endpoints are inside the rectangle.
The preview and selected geometry are highlighted, and the status bar reports
the number of selected vertices and lines. View filters are respected.

**Shift-drag** starts a selection rectangle anywhere and adds to the existing
geometry selection. **Shift-click** toggles an individual vertex or line.
Drag a selected vertex or line to move the entire group. Arrow keys nudge the
group; Delete removes it. Shared endpoints move once, preserving the group's
shape. Movement and deletion each support undo/redo, including restoring the
selection. **Escape** cancels a rectangle and restores the earlier selection.

Single clicks and dragging individual objects or polygons keep their previous
behavior. Use Shift-drag to start a selection box inside a polygon.

# Inset Boxes

Draw a box inside an existing normal polygon, including along its edges or corners, to partition it into the
inset center and connected surrounding polygons. Axis-aligned rectangles use
up to five regions (center, top, bottom, left and right), skipping empty regions; other outlines use convex
regions around the center. Adjacent pieces merge whenever their union remains
convex and fits the eight-vertex limit, eliminating unnecessary diagonals. Shared edges are reused or split without duplicates.
The center keeps the original polygon index and is selected after creation.
All regions retain the original floor/ceiling textures, origins, heights,
lighting, media, and sound settings. Boundary wall textures and neighboring
connections are preserved. Objects and annotations are reassigned to their
containing region. The entire partition is one undo/redo action.

The inset must stay within one outer polygon, but may share its edges or corners. Triangles, rotated shapes,
Circle-tool polygons, and irregular outlines are supported. Special polygon
types such as platforms, hidden containers, internal stray geometry, and splits
exceeding map limits are rejected. Boxes crossing existing walls remain blocked. The existing map is unchanged when placement is rejected.

# Circle Tool

Select **Circle** in the toolbar or press **C**. A **Tool options** panel appears
on the right with a **Sides** control (3–8); the setting is remembered. Drag
opposite bounding corners to draw a many-sided oval, or hold **Shift** to
constrain it to a circle. The preview shows the actual polygon vertices.

Vertices snap to nearby existing vertices. Shared edges are reused, and partial
shared edges are split without duplicate vertices or lines. Invalid overlaps,
crossings, concave snapping results, and polygon-limit violations are blocked
with an explanation in the status bar. **Escape** cancels. Creation is one
undoable action. Circles can also inset within an existing normal polygon,
including at edges and corners. The inset keeps the original polygon index;
surrounding pieces are merged into convex regions within the eight-vertex limit.

Circle insets preserve floor and ceiling settings, boundary textures, neighboring
connections, objects and annotations, just like Box insets. Objects outside the
actual circle are assigned to surrounding polygons. Nested circle and box insets
are supported. Shapes must remain within one normal polygon; crossing walls,
special polygon types and conflicting internal geometry are rejected.

# Move and Pan

**Pan (D)** drags the viewport. Hold **Space** for temporary panning, or use the
middle mouse button. Panning preserves the selection and does not edit geometry.

**Move (M)** drags the current selection by the mouse displacement, without
selecting something else at the click location. It supports vertices, lines,
selection groups, objects, annotations, and polygons. Moving a polygon also
moves its contained objects and annotations. Grid snapping applies to a common
translation, and each drag is one undoable action. Select items with **A** first.

## Visual geometry preferences

Preferences → Draw Mode includes **Line width (px)** (1–8, default 1) and
**Vertex size (px)** (2–16, default 2). Changes apply when you press OK and
are remembered for future sessions. Sizes stay constant as you zoom. Geometry
lines and drawing previews use the selected width; selected and preview
vertices stay larger than ordinary vertices. Grid lines remain one pixel wide.
These visual settings do not change geometry or selection/snap distances.

## Right-button move shortcut

Right-click and drag in geometry tools to move the current selection using
the Move tool's snapping and undo behavior. The active tool and selection stay
unchanged. An empty selection does nothing. Middle-button dragging still pans;
Alt-click remains available for picking paint values and alternate selection.

In Floor Height, Ceiling Height, Liquids, Ambient Sounds, Random Sounds,
Floor Textures, Ceiling Textures and Polygon Types, right-click picks up the
clicked polygon's value instead. The light paint modes also support this.
The palette updates immediately; right-drag samples polygons under the cursor.
Sampling changes no map data and adds no undo step. Left-click paints the picked
value, including None for liquids/sounds and the transfer mode for textures.

## Select hover and polygon groups

With Select active, moving the pointer previews the vertex, line or polygon
that a normal click will select in cyan. Hovering does not change the map or
selection. Leaving the map or changing tools clears the preview.

Shift-click toggles polygons as well as vertices and lines. Clicking inside an
already selected polygon keeps the group for dragging. Move, right-drag and
arrow-key nudging translate selected polygons together, including their objects
and annotations; shared vertices move once. Delete removes selected polygons
while keeping their boundary lines, as for single-polygon deletion. All group
edits support undo. Shift-drag still adds enclosed vertices and lines.

Preferences → Colors now includes **Hover Highlight Color**. It controls Select
hover previews for vertices, lines and polygons (polygon fills blend the hover
color with the normal polygon color). OK applies and saves it; Cancel keeps the
current color, and Reset Colors restores the default cyan.

Polygon hover now has its own **Polygon Hover Color** in Preferences → Colors.
The existing picker is labeled **Vertex/Line Hover Color**. Polygon previews use
the chosen polygon color directly. Both settings save on OK and reset with
Reset Colors; the previous polygon hover appearance is preserved until you
choose a separate color.

Edge and corner insets reuse perimeter vertices and lines. A box spanning the
full width or height can divide the surrounding area into disconnected regions;
each region is filled with valid convex polygons. Boxes extending outside the
container or crossing into neighboring polygons remain blocked.

## Undo and history

The lower-right History panel keeps up to 100 actions. Use its Undo and Redo buttons, or click a history entry to return to that state. Ctrl+Z undoes; Ctrl+Shift+Z or Ctrl+Y redoes.

A drag counts as one action. Failed operations and edits that change nothing do not add steps. Editing after undo discards the redo branch. Undo and redo restore map contents and the associated selection.

History resets when opening or switching maps and is not saved in the map file. When the limit is reached, “Earlier state” is the oldest retained state.

## Wall cleanup and paving

Special → Clean Up Wall Surfaces repairs wall side references, removes opaque textures from former exterior walls that now form open passages, and fills missing visible wall, floor and ceiling textures. Transparent wall textures are retained. Cleanup is undoable.

Pave Level runs the same cleanup and fills only missing visible wall, floor and ceiling textures. Existing textures, lighting, texture effects and transparent window/grate textures are retained. Clean Up Wall Surfaces also only fills missing textures. Open passages between equal-height polygons have no opaque wall surface to pave.

## Rotate tool

Select vertices, lines or polygons, then choose Rotate (R). Drag around the cross marking the selection's bounding-box center to rotate about the vertical axis. Hold Shift to snap to 15-degree increments. Start the drag away from the center for immediate rotation.

Selected polygons carry their objects and annotations along; player and monster facings rotate too. Shared endpoints rotate once, and each drag is one undo/redo step. A single selected vertex remains in place. Rotations that exceed map coordinate limits are ignored. Right-drag still moves the selection.

## Scale tool

Select vertices, lines or polygons, then choose Scale (S). Drag to stretch or compress X and Y independently around the center cross. Starting near a corner makes both axes follow the cursor directly. Hold Shift to preserve proportions and snap the uniform scale factor to 10% increments. Shift can be pressed or released during the drag; each update uses the original selection coordinates.

Selected polygons carry their objects and annotations; object facing and polygon heights stay unchanged. Shared endpoints scale once, and each drag is one undo/redo step. Scaling that would collapse geometry, duplicate an unselected vertex, flip or make an affected polygon concave, or exceed map coordinate limits, is ignored. Dragging across the pivot does not mirror the selection. Right-drag still moves the selection.

Paving uses an available wall collection from the loaded Shapes file and palette zero, rather than assuming the stock scenario's bitmap indices exist. Paving skips uniform-color placeholder bitmaps. If stock default indices are placeholders or absent, it chooses detailed bitmaps from that wall collection instead. If no wall collection is loaded, Pave, cleanup and Nuke Textures leave the map unchanged and explain how to load Shapes.
# Terminal editor

Use **Special → Level Terminals…** for embedded map terminals. Add a terminal,
then edit its text directly on the black terminal preview. Select text and use
**Bold**, **Italic**, **Underline**, or **Apply color** to format it. The eight
colors use Aleph One's standard palette. **Import text** replaces the current
page's text and formatting. **Apply** makes one undoable map edit; save normally
to keep it. Cancel discards the dialog's changes.

Each terminal has a stable zero-based ID. Set a wall's **Terminal** control-panel
permutation to that ID in the line parameters. New terminals append to the list;
existing IDs and unedited terminal bytes are retained. The numbered page list
reads top to bottom in file/playback order. Branch markers are labeled separately;
the engine chooses Unfinished, Success, or Failure according to completion state.
Use **Move earlier ↑** / **Move later ↓** to change the selected page's order.
Add, remove, and reorder pages/actions within a terminal. **Unfinished**, **Success**, and **Failure**
start completion branches; **End** closes a branch.

If a terminal has only display/action pages and no completion branch,
Apply adds **Unfinished** at the beginning and **End** at the end automatically.
For example: Unfinished → Logon → Picture → Logoff → End. The engine needs this
entry branch to activate the terminal. Explicit branches and Marathon 1 terminals
keep their existing structure.

Action/resource ID sets the
destination polygon, zero-based destination level, sound, tag, or picture ID,
depending on the page type. Text supports the engine's Mac Roman character set,
including Swedish letters; unsupported characters are rejected rather than lost.

The preview keeps the engine's standard **640 × 320 (2:1)** frame, including
when the dialog is maximized. **Preview zoom** enlarges the complete frame,
including text and pictures, from 100% to 300% (default 150%). Scroll to reach the
edges when the enlarged preview exceeds the window. Zoom is remembered and only
affects the editor display. Pictures retain their own proportions and shrink
to fit without stretching. Picture and logon/logoff pages load matching IDs
from the open map, its Mac resource fork, the configured scenario's map files,
and the Shapes file. **Picture resources…** selects another original scenario
Map/resource file when working on an exported level. That path is remembered.
Supported sources include picture WAD entries, MacBinary resource forks and
AppleDouble sidecars; supported images include classic PICT bitmap data
(indexed PackBits, RGB555 and RGB32), Windows `pict`/`clut` data and PNG/JPEG.
Missing or unsupported pictures report a reason below the preview.

**Import picture…** accepts PNG, JPEG and other GdkPixbuf-supported image files.
It fits the image to the selected left/right/centered area while retaining its
aspect ratio, composites transparency onto black, assigns an unused resource ID,
and updates the selected page. Apply, then save the map to embed the image as a
standard RGB32 PICT resource. Imports need no external image files afterward.
Cancel discards pending imports; Apply records them with the terminal edits in
one undo step. Existing resource WADs are preserved when exporting a level. Maps containing
resource entries use engine WAD version 4; one-entry maps use version 2. To repair
a map previously written with incorrect version 3, open it and save it again.

A checkpoint ID names a map goal rather than a picture; checkpoint-map previews
are not implemented. This editor does not import legacy external terminal text
resources. Fonts, pagination, and custom scenario chrome
remain approximate. Check the terminal in-game before distributing the map.

# Embedded Lua

Use **Special → Level Lua Scripts…** to add or import `.lua` files into the current
level, edit their names/source, export copies, reorder scripts, or remove them.
**Apply** commits one undoable edit; **Cancel** discards dialog changes. Save the
map normally to persist the scripts. Each level has its own script list, stored
in Aleph One's `LUAS` chunk; Atque is not needed to embed Lua this way.
Scripts run in list order in gameplay. They share Lua globals, so coordinate
trigger definitions when combining scripts. The editor stores source but does
not syntax-check or run it. Import UTF-8 text Lua files, not Lua bytecode.

Visual mode temporarily disables embedded gameplay Lua and uses only its own
locator script when needed. The original Lua chunk is restored exactly on return
and remains intact if visual mode fails. Existing names, flags and source bytes
are preserved when not edited. MML export remains a separate feature.

# Visual mode return

Returning from visual mode reloads the active paint palette, clears stale
selection references, and updates the scroll range. Newly introduced heights
and other paint values no longer cause missing-color dictionary exceptions;
drawing falls back to the normal polygon color if a palette entry is absent.

# MML export

Use **Special → Export Level MML…** to generate an Aleph One `.mml` script.
Collection loading has the engine's seven slots for the level's texture environment:
`-2` keeps the current scenario mapping, `-1` clears a slot, and `0–31` loads that
collection. Suggested extras use standard M2/Infinity mapping and leave the first
three scenario slots unchanged; check them against custom scenario MML. If more
than four extra collections are found, no automatic suggestion is applied: assign
the seven slots manually. Scenery collections must be chosen manually because the
editor does not know a custom scenario's scenery-to-collection mappings. Loading
a collection does not define new scenery types or combine different Shapes files.

Above-liquid and below-liquid fog each have an inclusion checkbox, enable switch,
color, depth in world units, and landscape toggle. Unchecked inclusion leaves the
scenario's fog unchanged. Include settings with fog disabled to explicitly turn
that fog off. Export does not modify the map or replace existing scripts.

Put the exported file in the scenario's **Scripts** folder and restart Aleph One.
External collection settings affect all levels using that texture environment;
fog affects all levels while the script is loaded. For settings limited to a single
level, embed the MML with Atque. Custom scenarios may need to merge these entries
into their existing scripts to control loading order. Fog is visible in Aleph One,
not in the editor's 2D view. This initial export dialog does not import scripts.

# Line tool polygon cuts

Draw a straight line from an existing polygon vertex or edge to another edge or
vertex to divide the polygon automatically. Starting or ending partway along an
edge creates a shared vertex. A cut can cross adjacent convex polygons and splits
each one it passes through. Drawing along an existing edge reuses the geometry.
Ordinary lines drawn outside polygons can also attach to their edges.

The resulting polygons retain heights, textures and other settings; objects and
annotations move to the appropriate polygon. One completed cut is one undo step.
Changing tools cancels an unfinished line. Incomplete cuts ending inside a polygon,
concave cuts, nonintegral edge crossings, and geometry exceeding the eight-vertex
limit are rejected without leaving partial geometry.

## Circle tessellation

Select the Circle tool and enable **Tessellate** in its options. Set **Sides**
and **Edge length (WU)**, then click and drag to expand a connected tile patch.
The preview shows all tiles before release; the status bar shows their count or
why placement is blocked. Release creates and selects the whole patch in one
**Tessellate polygons** undo step. Escape or changing tools/options cancels it.

Triangles, squares, and hexagons tile directly. Eight sides makes the standard
octagon-and-square pattern, including square fillers between four octagons.
Five and seven sides remain available for single circle polygons, but do not
have a tessellation pattern. Triangles are added in paired cells; staggered rows
and columns can give the patch a stepped outline.

The first tile starts from the snapped click position. Edge lengths use map
world units (1 WU = 1024 map coordinates). Triangle/hexagon lattice dimensions
are rounded once to integer coordinates, so adjacent tiles share identical
vertices even at small sizes. Dragging in either direction expands from the
first cell; reaching the next cell step adds a row or column.

Patterns add tiles in empty space and can connect to existing boundaries.
Matching existing tiles and shared lines/vertices are reused. Tessellation does
not inset or cut across existing polygons: any conflicting tile blocks the
whole patch. Patches are limited to 256 polygons per drag; larger areas can be
built with repeated drags. Size and tessellation mode are remembered.

## Filled shapes and outlines

Both Box and Circle have a **Filled** checkbox, enabled by default and remembered
independently. Checked creates polygons with the existing inset behavior.
Unchecked creates only boundary vertices and lines and selects those lines.
Circle tessellation also follows this setting: unfilled patches keep their shared
lines, without creating polygons. A complete drag remains one undo step.

Outlines reuse existing boundaries and split shared walls when attaching to
empty space. They cannot draw unsplit geometry through or inside an existing
polygon; use Filled for polygon insets. Repeating an outline reuses its geometry
without adding an undo step. Drawing the same shape with Filled enabled can fill
an existing empty outline, and the Fill tool remains available.

## Merged scenarios

Open a merged `.sceA` scenario as usual. The **Level** menu lists maps by their
original scenario IDs and names, with the active map checked. Resource entries
are not listed as maps. Files whose first map is not ID 0 are supported.

Switching maps retains the current edits in memory. **Save** writes all maps back
to the opened scenario, and **Save As** creates a complete scenario copy rather
than exporting only the selected map. Closing or opening another file prompts
for changes from both the current map and maps edited earlier in the session.
Undo history starts fresh when switching maps, as in the original map selector.

Unedited maps retain their original chunk bytes and directory metadata. Edited
map IDs remain unchanged, so level transfers keep their targets. Per-level Lua,
terminal data, custom chunks, and shared WAD resources are retained. Imported
terminal picture IDs are allocated across the whole scenario; resources still
referenced by other maps are kept. Visual mode continues to edit only the active
map and returns its changes to the scenario session.

Saves write a complete temporary file before replacing the destination. Native
Mac resource forks are copied when saving on macOS. External scenario assets
such as Shapes and Sounds remain separate files.
