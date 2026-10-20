## Forge Ahead — 20261020

### Camera paths

- A dedicated Camera Paths tool for creating and editing named camera movements.
- Right-click to add waypoints; left-click to select and left-drag to move them.
- Numbered nodes and direction arrows show waypoint order and camera facing.
- Edit absolute height, facing, pitch, travel time and the final hold duration.
- Play or stop named paths through polygon Lua presets. Playback returns to the player when finished.
- Paths support undo and survive saving, reopening, and returning from visual mode.

### Captions

- Add caption text and duration to individual waypoints.
- Separate Waypoints and Captions tabs make the controls easier to find.
- Caption position, size, RGB color and fade-in/out controls are available for Sprintathon with the styled-caption engine patch. Older engines show ordinary HUD text.

### Fixes

- Fixed camera-node redraws so dragging updates live, with one undo step per drag.
- Coalesced pointer events and moved expensive whole-path validation to a Check path button.
- Fixed the Space key switching tools while typing in sidebar text fields.
- Fixed a startup crash when GTK exposes internal widgets without managed wrappers.

### Notes

Camera segments are straight. Check paths before playback, especially around walls or overlapping rooms. The active player can still move during playback, and camera playback does not resume from saved games.

Experimental baked lighting remains available. Complex geometry may still produce inaccurate shadows or extra subdivisions; keep a backup before baking.

Packages target Linux x64, Windows x64, macOS Intel and Apple Silicon. .NET is bundled. Windows and Mac include GTK; Linux requires system GTK 3.
