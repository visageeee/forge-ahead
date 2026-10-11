using System;
using System.Collections.Generic;

namespace Weland
{
    public partial class Editor
    {
        bool ringStairs, circleStairs, ringCeilingFollows, circleCeilingFollows;
        short ringStairStart, circleStairStart, ringStairStep = 128, circleStairStep = 128, ringStairCeiling = 1024, circleStairCeiling = 1024;
        public bool RingStairs { get => ringStairs; set { if (value != ringStairs) { CancelRing(); ringStairs = value; } } }
        public short RingStairStart { get => ringStairStart; set { if (value != ringStairStart) { CancelRing(); ringStairStart = value; } } }
        public short RingStairStep { get => ringStairStep; set { if (value != ringStairStep) { CancelRing(); ringStairStep = value; } } }
        public bool CircleStairs { get => circleStairs; set { if (value != circleStairs) { CancelCircle(); circleStairs = value; } } }
        public short CircleStairStart { get => circleStairStart; set { if (value != circleStairStart) { CancelCircle(); circleStairStart = value; } } }
        public short CircleStairStep { get => circleStairStep; set { if (value != circleStairStep) { CancelCircle(); circleStairStep = value; } } }

        public short RingStairCeiling { get => ringStairCeiling; set { if (value != ringStairCeiling) { CancelRing(); ringStairCeiling = value; } } }
        public bool RingCeilingFollows { get => ringCeilingFollows; set { if (value != ringCeilingFollows) { CancelRing(); ringCeilingFollows = value; } } }
        public short CircleStairCeiling { get => circleStairCeiling; set { if (value != circleStairCeiling) { CancelCircle(); circleStairCeiling = value; } } }
        public bool CircleCeilingFollows { get => circleCeilingFollows; set { if (value != circleCeilingFollows) { CancelCircle(); circleCeilingFollows = value; } } }

        public static bool ApplyStairHeights(Level plan, List<short> selected, List<int> steps,
            short start, short step, short ceilingStart, bool ceilingFollows, out bool changed, out string reason)
        {
            changed = false; reason = null;
            if (selected.Count != steps.Count)
            { reason = "Stair heights do not match the generated polygons."; return false; }
            var heights = new Dictionary<short, (short Floor, short Ceiling)>();
            for (int i = 0; i < selected.Count; ++i)
            {
                long offset = (long)step * steps[i];
                long height = (long)start + offset;
                long ceiling = (long)ceilingStart + (ceilingFollows ? offset : 0);
                if (height < short.MinValue || height > short.MaxValue || ceiling < short.MinValue || ceiling > short.MaxValue)
                { reason = "Stairs exceed the map height limits: reduce the floor/ceiling height or step height."; return false; }
                if (height >= ceiling)
                { reason = "A stair floor reaches its ceiling: lower the floor, raise the ceiling, or enable Ceiling follows floor."; return false; }
                if (heights.TryGetValue(selected[i], out var existing) && (existing.Floor != height || existing.Ceiling != ceiling))
                { reason = "Conflicting stair heights for the same polygon."; return false; }
                heights[selected[i]] = ((short)height, (short)ceiling);
            }
            // Validate every step before changing any floor or ceiling, including reused
            // polygons. Geometry and floor changes share the caller's undo step.
            foreach (var pair in heights)
            {
                var polygon = plan.Polygons[pair.Key];
                changed |= polygon.FloorHeight != pair.Value.Floor || polygon.CeilingHeight != pair.Value.Ceiling;
                polygon.FloorHeight = pair.Value.Floor;
                polygon.CeilingHeight = pair.Value.Ceiling;
            }
            return true;
        }
    }
}
