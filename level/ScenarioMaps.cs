using System;
using System.IO;
using System.Linq;

namespace Weland
{
    public static class ScenarioMaps
    {
        public static int[] Indexes(MapFile source) => source.Directory
            .Where(pair => pair.Value.Chunks.ContainsKey(MapInfo.Tag)).Select(pair => pair.Key).ToArray();

        public static MapFile.Overlay Overlay(Wadfile.DirectoryEntry entry)
        {
            using var reader = new BinaryReaderBE(new MemoryStream(entry.Chunks[MapInfo.Tag]));
            var info = new MapInfo(); info.Load(reader);
            return new MapFile.Overlay { LevelName = info.Name, MissionFlags = info.MissionFlags,
                EnvironmentFlags = info.EnvironmentFlags, EntryPointFlags = info.EntryPointFlags };
        }

        public static string Name(MapFile source, int index) =>
            source.Overlays.TryGetValue(index, out var overlay) ? overlay.LevelName : Overlay(source.Directory[index]).LevelName;

        public static MapFile Update(MapFile source, int index, Wadfile.DirectoryEntry edited)
        {
            if (!edited.Chunks.ContainsKey(MapInfo.Tag) || (source.Directory.TryGetValue(index, out var old) && !old.Chunks.ContainsKey(MapInfo.Tag)))
                throw new InvalidDataException("The selected scenario entry is not a map.");
            var output = new MapFile();
            foreach (var pair in source.Directory) output.Directory[pair.Key] = pair.Value.Clone();
            foreach (var pair in source.Overlays)
                output.Overlays[pair.Key] = new MapFile.Overlay { LevelName = pair.Value.LevelName,
                    MissionFlags = pair.Value.MissionFlags, EnvironmentFlags = pair.Value.EnvironmentFlags,
                    EntryPointFlags = pair.Value.EntryPointFlags };
            output.Directory[index] = edited.Clone();
            output.Overlays[index] = Overlay(edited);
            foreach (int key in output.Directory.Keys)
                if (!output.Overlays.ContainsKey(key)) output.Overlays[key] = output.Directory[key].Chunks.ContainsKey(MapInfo.Tag)
                    ? Overlay(output.Directory[key]) : new MapFile.Overlay { LevelName = "" };
            return output;
        }

        // Replace the destination only after a complete, successful write.
        public static void SaveAtomic(MapFile source, string filename, string originalFilename = null)
        {
            filename = Path.GetFullPath(filename);
            string temporary = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileName(filename) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                source.Save(temporary);
                // Native Mac resource forks belong to the file inode, so retain the
                // original fork when replacing a legacy scenario or saving a copy.
                string original = !string.IsNullOrEmpty(originalFilename) ? originalFilename : filename;
                if (OperatingSystem.IsMacOS() && File.Exists(original))
                {
                    string fork = original + "/..namedfork/rsrc";
                    if (File.Exists(fork))
                    {
                        using var input = File.OpenRead(fork);
                        using var output = File.Create(temporary + "/..namedfork/rsrc");
                        input.CopyTo(output);
                    }
                }
                File.Move(temporary, filename, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
