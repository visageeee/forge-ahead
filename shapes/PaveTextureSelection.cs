using System;
using System.Collections.Generic;

namespace Weland
{
    public static class PaveTextureSelection
    {
        public static bool HasTextureDetail(Image image)
        {
            if (image.Data.Length < 6) return false;
            for (int i = 3; i < image.Data.Length; i += 3)
                if (image.Data[i] != image.Data[0] || image.Data[i + 1] != image.Data[1] || image.Data[i + 2] != image.Data[2]) return true;
            return false;
        }

        public static bool TryChoose(ShapesFile shapes, int preferredCollection,
            out ShapeDescriptor wall, out ShapeDescriptor floor, out ShapeDescriptor ceiling)
        {
            wall = floor = ceiling = ShapeDescriptor.Empty;
            if (shapes == null) return false;
            var candidates = new List<int>();
            if (preferredCollection >= 0 && preferredCollection < ShapeDescriptor.MaximumCollections) candidates.Add(preferredCollection);
            for (int i = 0; i < ShapeDescriptor.MaximumCollections; ++i)
                if (i != preferredCollection) candidates.Add(i);
            foreach (int index in candidates)
            {
                Collection collection = shapes.GetCollection(index);
                if (collection.Type != CollectionType.Wall || collection.ColorTableCount == 0) continue;
                var usable = new List<byte>();
                for (int bitmap = 0; bitmap < Math.Min(256, (int)collection.BitmapCount); ++bitmap)
                {
                    var descriptor = new ShapeDescriptor((ushort)(index << 8 | bitmap));
                    // Custom scenarios can keep stock IDs as solid-color
                    // placeholders. Exclude them from automatic defaults.
                    if (HasTextureDetail(shapes.GetShape(descriptor))) usable.Add((byte)bitmap);
                }
                if (usable.Count == 0) continue;
                ShapeDescriptor Choose(byte preferred, int fallback) =>
                    new ShapeDescriptor((ushort)(index << 8 | (usable.Contains(preferred) ? preferred : usable[Math.Min(fallback, usable.Count - 1)])));
                wall = Choose(5, 0); floor = Choose(6, 1); ceiling = Choose(7, 2);
                return true;
            }
            return false;
        }
    }
}
