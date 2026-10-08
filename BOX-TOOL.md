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

Draw a box strictly inside an existing normal, axis-aligned rectangular polygon
to partition it into five connected polygons: the inset center plus top, bottom,
left, and right sections. Shared edges are reused or split without duplicates.
The center keeps the original polygon index and is selected after creation.
All five regions retain the original floor/ceiling textures, origins, heights,
lighting, media, and sound settings. Boundary wall textures and neighboring
connections are preserved. Objects and annotations are reassigned to their
containing region. The entire partition is one undo/redo action.

This first version requires the inset to be fully inside the outer rectangle.
Nonrectangular polygons, special polygon types such as platforms, hidden
containers, internal stray geometry, and splits exceeding polygon limits are
rejected. The existing map is unchanged when placement is rejected.

# Circle Tool

Select **Circle** in the toolbar or press **C**. A **Tool options** panel appears
on the right with a **Sides** control (3–8); the setting is remembered. Drag
opposite bounding corners to draw a many-sided oval, or hold **Shift** to
constrain it to a circle. The preview shows the actual polygon vertices.

Vertices snap to nearby existing vertices. Shared edges are reused, and partial
shared edges are split without duplicate vertices or lines. Invalid overlaps,
crossings, concave snapping results, and polygon-limit violations are blocked
with an explanation in the status bar. **Escape** cancels. Creation is one
undoable action. Circle insets into existing polygons are currently blocked;
automatic inset subdivision remains specific to the Box tool.

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

Right-click and drag anywhere in the map to move the current selection using
the Move tool's snapping and undo behavior. The active tool and selection stay
unchanged. An empty selection does nothing. Middle-button dragging still pans;
Alt-click remains available for picking paint values and alternate selection.
