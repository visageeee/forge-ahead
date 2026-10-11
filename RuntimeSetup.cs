using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Weland
{
    // Portable Windows GTK bundles store native DLLs beside the app executable.
    // Configure GTK before any GtkSharp method is JIT-compiled or called.
    internal static class RuntimeSetup
    {
        public static void Configure()
        {
            if (!OperatingSystem.IsWindows()) return;
            string root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string template = Path.Combine(root, "loaders.cache.in");
            if (!File.Exists(template)) return; // Normal developer/system GTK installation.
            Environment.SetEnvironmentVariable("GTK_DATA_PREFIX", root);
            Environment.SetEnvironmentVariable("GTK_EXE_PREFIX", root);
            Environment.SetEnvironmentVariable("FONTCONFIG_PATH", Path.Combine(root, "etc", "fonts"));
            Environment.SetEnvironmentVariable("GIO_EXTRA_MODULES", Path.Combine(root, "lib", "gio", "modules"));
            Environment.SetEnvironmentVariable("XDG_DATA_DIRS", Path.Combine(root, "share"));
            Environment.SetEnvironmentVariable("GSETTINGS_SCHEMA_DIR", Path.Combine(root, "share", "glib-2.0", "schemas"));
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Weland-SE", "gtk-cache", id);
            Directory.CreateDirectory(cacheDir);
            string cache = Path.Combine(cacheDir, "loaders.cache");
            string content = File.ReadAllText(template).Replace("@GTKROOT@", root.Replace('\\', '/'));
            // Atomic replacement also allows multiple Weland instances to start together.
            string temp = Path.Combine(cacheDir, Guid.NewGuid().ToString("N"));
            File.WriteAllText(temp, content);
            File.Move(temp, cache, true);
            Environment.SetEnvironmentVariable("GDK_PIXBUF_MODULE_FILE", cache);
        }
    }
}
