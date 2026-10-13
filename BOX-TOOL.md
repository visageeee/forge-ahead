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

## Select tool options

The Select sidebar offers Select all and Deselect all. Ctrl+A selects every
vertex, line, and polygon in the map; Ctrl+Shift+A clears the selection. Text
fields retain their normal Ctrl+A text-selection behaviour. Selecting alone
does not edit the map or add undo history.

Mirror horizontal reflects left/right, and Flip vertical reflects top/bottom,
around the selected geometry's bounding-box centre. Selected polygons carry
their objects and annotations, and player/monster facings reflect too. Shared
vertices transform once. Polygon winding and wall-side ownership are repaired
while preserving textures and polygon settings. Reflections that overlap or
fold existing geometry are rejected without changing the map; include connected
neighbours when needed. Each reflection is one undo step.

Shift height (WU) adds the entered amount to Floor, Ceiling, or both. Positive
values raise and negative values lower. Explicitly selected polygons take
priority; when only vertices/lines are selected, attached polygons are used.
Shifts preserve textures, validate the whole selection against height limits
and inverted floor/ceiling order, and form one undo step.

## Ring tool

Choose Ring beside Circle in the toolbar, then drag opposite corners. The right
panel offers 3–128 sides (16 by default). Hold Shift to constrain the outline
to a circle; otherwise it can be elliptical.

Enable Inner rings and choose a count from 1–16. Innermost size (%) sets the
smallest ring relative to the outer ring; intermediate rings are evenly spaced.
Corresponding vertices connect into convex quadrilateral bands, leaving the
centre open. For example, three inner rings with an innermost size of 25%
produce boundaries at 75%, 50%, and 25% of the outer radius. Without Inner rings,
sectors connect to the centre. Each drag supports at most 256 sectors, so the
side count multiplied by the inner-ring count must be at most 256.

Filled creates polygon sectors. Uncheck Filled for just the ring boundaries
and connecting spokes, with no new polygons. An existing matching outline can
be filled later by drawing the same ring with Filled enabled. Individual
polygons stay below the engine's eight-vertex limit even with many outer sides.

The preview shows shared spokes and boundaries without changing the map.
Placement is checked on release: sectors share existing matching vertices and
lines, identical sectors are reused, and overlaps or collapsed/narrow sectors
are rejected atomically. Geometry inside the open centre is preserved. Rings
add geometry in empty space; this tool does not inset into existing polygons.
The complete ring is selected and creation is one undo step. Escape or changing
the tool/options cancels the preview. Ring options are saved in preferences.

## Stairs for rings and tessellation

Both tools offer Stairs, Starting height (WU), Step height (WU), Ceiling height
(WU), and Ceiling follows floor. Stairs
requires Filled; in the Circle tool it also requires Tessellate. The starting
height is an absolute floor height. Each next step adds the step height;
negative values descend. The default step height is 0.125 WU. Values are stored
at the map's 1/1024-WU precision.

Ring stairs start at the top of the ring and step clockwise. Matching angular
sectors across all concentric bands share a height. The last sector meets the
first at the top, making a height break there for nonzero steps.
Tessellation steps along the longer drag axis (X when tied), starting at the
initial tile and advancing toward the drag endpoint. Tiles in the same lattice
row or column share a height; paired triangles share a step. Octagon square
fillers use the greater step index of their neighbouring rows/columns.

Matching existing polygons also receive the specified floor and ceiling
heights. Ceiling height is absolute: unchecked Ceiling follows floor keeps it
fixed; checked, it adds the same step offset as the floor, retaining constant
headroom. Textures are preserved. A floor reaching its ceiling, or a floor or
ceiling outside the map's range, rejects the entire operation without changing
the map. Creation and height
changes form one undo step. Stair settings are saved independently for each tool.

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

Visual mode runs embedded gameplay Lua, including polygon triggers. Its
locator script preserves existing callbacks and positions the player before initialization. The original Lua chunk is restored exactly on return
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

### Copy and paste selections

Use **Ctrl+C** to copy selected vertices, lines, or polygons, and **Ctrl+V**
to start placing a copy. The Select sidebar also has Copy and Paste buttons.
Move the pointer to position the preview, then left-click to place it; Escape
or right-click cancels. Placement snaps the selection centre to the grid when
snapping is enabled. Copy again only when you want to replace the clipboard;
repeated pastes use the original snapshot.

Polygons include their boundary geometry, settings, wall textures, contained
objects and annotations. Individual objects or annotations can also be copied
and pasted inside a polygon. Shared vertices and lines are preserved and reused
when pasting against matching existing edges. Overlapping polygons, crossing
lines and coordinate overflow are rejected without changing the map. A paste
selects the new geometry and adds one undo/history step.

Platform settings and references between copied polygons follow their copies.
Existing terminal, texture, light, sound and media IDs remain level-local; this
is an internal clipboard for the current level, cleared when changing levels
or opening another map. It does not exchange geometry with other applications.

## Line tool: split all intersected polygons

Enable **Split all intersected polygons** in the Line tool's sidebar, then drag
through the polygons to cut. The option is off by default and remembered between
sessions. When enabled, the start and end can be outside the map geometry;
each convex polygon crossed from boundary to boundary is split. Vertices are
created at crossings, shared walls use a single shared vertex, and the complete
path is connected through empty gaps and beyond the outer walls. Loose lines
are split at crossings too. Existing collinear lines and vertices are reused.

The entire path is one **Split intersected polygons** undo action. Polygon
settings, wall textures, objects and notes follow the resulting polygons.
Existing matching vertices and path segments are reused.
Escape, switching tools, or changing the checkbox cancels an unfinished line.
Failures restore the complete original map, including deferred start vertices.

Start and end outside polygons or on their boundaries. Ending inside a polygon
would leave an incomplete cut and is rejected. Crossings between map coordinates
are rounded to the nearest coordinate in each axis (at most half a coordinate
per axis). Both sides of a shared wall use the same point. A sloping wall may
bend slightly through that point, while its original endpoints remain in place;
this avoids jumping to distant vertices on sparse integer edges. If rounding
cannot produce valid convex polygons and a connected path without crossings,
the cut is rejected atomically. Cuts exceeding the engine's eight-vertex limit
are rejected.
With the checkbox disabled, the existing boundary-to-boundary cut behavior remains.

The Line tool shares Select's geometry targeting: vertices take priority over
edges, with the same zoom-aware snap distance, visible-geometry filters, and
hover colors. Polygon interiors use the polygon hover color. The hovered edge
or vertex remains highlighted during a drag; preview and release resolve the
same endpoint. Edges snap to the nearest projected map coordinate, including
sparse sloping edges. A tiny bend may be needed when attaching to a sloping
edge. Grid snapping applies when no nearby geometry is targeted. The separate
vertex-snap checkbox and edge-snap distance field are no longer needed.

Sidebar numeric inputs commit and release keyboard focus on Enter. Clicking
the map, tools, or other non-input controls also releases input focus so tool
hotkeys work again. Text editing keeps its normal shortcuts while focused.


### Penrose tessellation

In the Circle tool, enable **Tessellate** and choose **Pattern → Penrose rhombs**.
Set **Edge length (WU)**, then click and drag to expand a patch of thin and thick
rhombi. Every tile is a convex four-sided polygon; the Sides field applies only
to regular patterns. Filled, outline, and Stairs work as for other tessellations.
The pattern choice is saved in preferences.

The drag selects complete tiles by their centers with a one-edge margin, so the
outer border is jagged
and can extend past the drag rectangle. A fixed six-generation sun seed keeps
the tiling stable while dragging and allows matching patches at the same origin
to be extended or reused. Patches are limited to 256 tiles and roughly sixteen
edge lengths from the origin per axis; increase Edge length for a larger area.
Shared vertices use the same rounded map coordinates. Map bounds, convexity,
overlap, and height limits are checked before committing; the entire patch is
one undo step.

Construction uses golden-ratio Robinson triangle subdivision and joins paired
halves into Penrose rhombi. Reference: [Bielefeld University's Tilings
Encyclopedia](https://tilings.math.uni-bielefeld.de/substitution/robinson-triangle/).


### Interactive performance

Map drawing batches polygons, edges, vertices, and grid marks with the same
style instead of issuing a separate Cairo paint for every item. Very dense
standard and custom grids show fewer lines when zoomed out; grid snapping keeps
the configured spacing. Hover, pan, selection, and transform pointer updates
are coalesced to the newest sample about every 16 ms. The pending sample is
processed before a mouse release, scroll, or special key; painting modes retain
every pointer sample so strokes do not skip surfaces. Moving a group updates
all vertices before recalculating each affected polygon once per update.

Headless benchmarks on a synthetic 1,800-polygon/4,000-edge/2,500-vertex scene
measured roughly 17.0 ms per frame before and 13.5 ms after, with identical
rendered pixels. Moving a selected 256-polygon grid measured approximately
0.59 ms per update before and 0.22 ms after, with allocations reduced from
131 KB to 60 KB per update. These measurements exclude GTK event handling and
window compositing; desktop responsiveness still needs interactive verification.


### Polygon Lua entry triggers

Select one or more polygons and use **Polygon Lua trigger…** in the Select
sidebar. Enable the trigger and enter a Lua code body. `player` is the player
who entered and `polygon` is the entered polygon. For example:

```lua
player:print("Welcome to the reactor")
-- Or call a function defined in Level Lua Scripts:
-- enter_reactor(player, polygon)
```

Apply replaces the binding on every selected polygon. **Enabled** can disable
it while retaining the code; **Remove** clears it. **Once per level** fires only
for the first player entering each assigned polygon, shared by all players.
Starting or respawning in a polygon counts as entry. Staying inside does not
repeat the trigger; leaving and entering again fires it again unless Once is
checked. The generated dispatcher checks player polygons after each engine
tick. Extremely thin polygons crossed completely between checks may be missed.

Assignments preserve polygon type and permutation. They follow polygon
renumbering, copy/paste, and undo; split/inset pieces inherit the original
binding and each piece has its own Once state. Function calls still require the
named function to exist in that level's Lua scripts. Lua syntax is checked by
the engine, not the dialog.

Save embeds a generated **Forge Ahead Polygon Triggers** Lua script after the
other embedded scripts and stores editable assignments in the level's `FLUA`
chunk. The generated script is read-only in Level Lua Scripts; edit it through
the Select sidebar. Other scripts are preserved, and existing `Triggers.init`
and `Triggers.postidle` callbacks are called. Once state uses Game custom
fields and is restored from a saved game; existing init callbacks run after
this restoration. Restoring a checkpoint does not itself count as entry.
Bindings and generated code are protected during visual mode. No external Lua
file or special polygon type is required.

The engine events and saved custom fields are described in the official
[Aleph One Lua reference](https://github.com/Aleph-One-Marathon/alephone/blob/master/docs/Lua.html).


### Premade polygon Lua actions

**Polygon Lua trigger… → Insert premade action** offers categorized presets.
Select a category and action, enter its parameters, then **Insert at cursor**.
The generated code is editable and can be combined with other actions before
Apply. Inserting does not change the map until Apply.

- **Ammo & weapons:** refill all ammo, give all weapons and ammo, or add packs
  of a particular ammunition item.
- **Health & oxygen:** instant healing, refill health to a cap, gradual healing,
  and oxygen refill. Health caps use points: 150 is one normal shield bar, 450
  is three. Healing never lowers health already above the chosen cap.
- **Sounds:** play a sound to the entering player, or a positional sound from a
  selected polygon. Sound ID and pitch are configurable.
- **Monsters:** choose monster type ID, destination polygon ID, and count
  (1–16). Monsters spawn at the destination's floor and are activated. Set a
  matching Shapes collection ID to preload graphics if the collection is not
  already loaded; -1 means it is already loaded. Retain the generated preload
  comment so the editor can put its collection request outside the callback.
  Monster types, collections, sounds and inventory IDs follow scenario/physics
  definitions; stock ID labels are provided as examples.
- **World:** teleport the player, activate/deactivate lights, or activate/
  deactivate platform polygons (including doors).
- **Messages:** display text to the entering player, with Lua quoting handled
  automatically.

A polygon ID of -1 uses the triggering polygon. Explicit polygon/light IDs in
code are literal values; check them after deleting or renumbering target
geometry. Trigger assignments themselves continue to follow their polygons.

Gradual healing uses points per second and a total healing amount. It continues
after leaving, stops at the cap or when its amount is exhausted, and stops on
death. Re-entering restarts the effect unless Once per level prevents another
activation. Effects on different players are independent. The dispatcher
processes healing every engine tick and stores the remaining amount and partial
second in Player custom fields so checkpoints can restore an unfinished effect.
Only one gradual-healing effect runs per player; a new one replaces the old one.

Useful future presets include timed doors, fog transitions, objectives and
monster waves.

Lua trigger polygons are tinted pink in normal drawing mode when enabled and nonempty. Selection, hover, destination and invalid-polygon colors take priority; height/texture modes retain their own colors.

The Spawn monsters preset averages the destination polygon's vertices for its position, avoiding stale stored centers. A zero floor-relative spawn height places monsters on the current floor, including raised/lowered floors and platforms. Unchanged older Spawn monsters preset blocks are repaired when loading or saving. Reopen and save existing maps before testing in the engine. Custom-edited spawn code is preserved.

Spawn actions now automatically preload their monster graphics using embedded MNpx physics, or standard Marathon 2/Infinity collections when no embedded definition exists. Existing preset calls are recognized on save. External physics and nonstandard scenarios may require the extra collection ID. A new game after saving is required to load new graphics.

The Fog category offers Set and enable, Enable, Disable, Change color, and Change depth. Choose normal or underwater fog; color channels use 0–1 and depth uses world units (smaller is denser). These change the view's fog, not the polygon's geometry, and persist until another action changes it.

Fog > Transition fog smoothly interpolates color and depth over a duration in seconds (0.1–600). Each layer has one transition; another replaces it from its current values, and immediate fog actions cancel it. Transitions advance once per 30 Hz engine tick and retain progress through saved games. Starting with disabled fog enables it from a distant depth of 1024 world units.

Lua action fields now offer named dropdowns beside editable IDs for monsters, sounds, ammo, collections, polygons and lights. Names describe standard Marathon assets; custom scenarios may repurpose IDs. Unlisted IDs remain available through the numeric field. Fog layer and landscape choices have dropdowns too.

Fog transitions interpolate inverse depth (fog strength), avoiding an almost-clear view followed by a sudden thickening when fading from very distant fog. Use **Transition fog smoothly** and set its duration; **Set fog instantly** deliberately makes an immediate change. Existing transition actions use the improved interpolation after saving the map.

The polygon Lua dialog has premade actions in a scrollable left sidebar with a prominent Insert at cursor button, and the script on the right. Apply script action to polygon saves the complete script to the selected polygons; Remove script action from polygon clears their triggers.

Start monster spawner at polygon spawns immediately and then at the selected interval in seconds. Maximum total monsters counts successful creations; Indefinite ignores that total limit. It runs after leaving the trigger. Re-entering does not reset an existing or completed spawner. Stop monster spawner at polygon removes it, allowing a later start to begin again. Progress survives saved games; a new level resets it. One spawner is supported per destination polygon. Graphics preload automatically as for Spawn monsters.

The polygon Lua dialog places Insert, Cancel, Remove, and Apply on one footer row. Both script editors color Lua keywords, strings, comments, and numbers without changing source text.

Timing > Run an action after a delay wraps selected Lua code, or inserts a default delayed message. The queued action receives the original player and triggering polygon; other local variables are not captured. It runs after the specified seconds even after leaving the polygon, unless the original player is dead or gone. Queued source and timers survive saved games.

Logic > Set a named flag writes a level-wide boolean. Only run if a flag is set wraps selected code (or inserts a placeholder). Unset flags are false. Flags survive saved games and reset on a new level. Names are case-sensitive.

Monsters > Start enemy waves uses one encounter per destination polygon, with monster type, monsters per wave, wave count and pause after defeat. The next wave waits for all tracked monsters in the current wave to die or disappear. Failed spawns retry once per second without consuming the quota. Re-entering does not restart an encounter. Encounter progress survives saved games.

### Light Lua actions

The Lights category includes Dim all lights and Brighten all lights. Amount is
in percentage points (20 changes 70% to 50% when dimming). Every active and
inactive light state's base intensity is adjusted and clamped to 0–100%.
Existing functions, random variation, periods, tags and activation status remain
unchanged. Changes take effect on the next light phase, not necessarily instantly.
Random variation can still exceed the base intensity. Light-controlled liquid
heights can also change. Repeated triggers accumulate; use Once per level for a
single adjustment. These actions can be wrapped in Delay or a flag condition.

Transition seconds on Dim/Brighten all lights defaults to zero (preserve existing
light timing). A positive duration sets each state's function to smooth and its
period to that duration, without random period variation. The first transition
starts at the next phase boundary; this is not a synchronized whole-map fade.
These timings remain in effect after the transition, replacing flicker settings.

When returning from visual mode with embedded Lua, pre-existing light definitions
are restored from the editor's original map. Runtime changes cannot become the
map's starting lighting. Added lights and surface light assignments are retained.
Edits to existing light definitions during scripted visual-mode sessions are also
discarded; edit those definitions in Forge Ahead instead.

### Directing monsters

Monsters → Move monsters between polygons provides source and destination polygon
dropdowns and editable IDs. Source -1 means the triggering polygon. It issues one
pathfinding order to every living non-player monster in the source at execution
time, including friendly monsters. The engine activates inactive monsters. Dead
monsters, players and monsters elsewhere are skipped; equal source/destination
is a no-op. A traversable route is required, and normal combat AI can override
the order. This does not teleport monsters or guarantee arrival. Delay and flag
wrappers can be used; delayed orders check occupants when the delay expires.

Movement presets include never-activated monsters whose engine vitality is -1,
while excluding death animations. Reinsert an older movement action to update
its vitality filter.

Move monsters between polygons has a Stop at destination checkbox. It deactivates
each ordered monster on entering the destination polygon (not its exact center).
This is a one-time stop: later sounds, sight or damage may reactivate it.
Arrival tracking survives saves and clears on death. Teleport-out-on-deactivation
is suppressed for this stop. A newer movement order replaces the pending stop.

Movement orders now retry individually until arrival, death, or a two-minute
timeout. Retries are staggered and limited to one per tick. Moving monsters get
three seconds between retries; stationary monsters one second. This helps when
activation/combat replaces paths or a path request temporarily fails. Blocked
routes and crowd collisions can still prevent arrival. Reinsert old movement
actions to enable this tracking.
