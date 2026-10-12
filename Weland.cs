using Gtk;
using System;
using System.Text;

namespace Weland
{
    public static class PlatformDetection
    {
        public static bool IsMac => OperatingSystem.IsMacOS();
        public static bool IsX11 => Environment.OSVersion.Platform == PlatformID.Unix && !IsMac;
        public static bool IsWindows => OperatingSystem.IsWindows();
    }

    public delegate void ShapesFileChangedEventHandler();

    public class Weland
    {
        public static Settings Settings = new Settings();

        static ShapesFile shapes;
        public static Plugins plugins = new Plugins();

        public static event ShapesFileChangedEventHandler ShapesChanged;

        public static ShapesFile Shapes
        {
            get { return shapes; }
            set
            {
                bool changed = (shapes != value);
                shapes = value;
                if (changed && ShapesChanged != null)
                {
                    ShapesChanged();
                }
            }
        }

        static void OnUnhandledException(Exception outer)
        {
            Exception e;
            if (outer.InnerException != null)
            {
                e = outer.InnerException;
            }
            else
            {
                e = outer;
            }

            using var d = new MessageDialog(null, DialogFlags.DestroyWithParent, MessageType.Error, ButtonsType.Close, e.Message);
            d.Title = "Unhandled Exception";
            d.SecondaryText = e.StackTrace;
            d.Run();
        }

        public static void OnUnhandledException(object o, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            OnUnhandledException(e);
        }

        public static void OnUnhandledException(GLib.UnhandledExceptionArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            OnUnhandledException(e);
            args.ExitApplication = true;
        }

        static void StartupTrace(string stage)
        {
            if (Environment.GetEnvironmentVariable("FORGE_AHEAD_STARTUP_TRACE") == "1")
                Console.Error.WriteLine("Forge Ahead startup: " + stage);
        }

        public static int Main(string[] args)
        {
            StartupTrace("configuring runtime");
            RuntimeSetup.Configure();
            return RunGtk(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int RunGtk(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnUnhandledException);
            GLib.ExceptionManager.UnhandledException += new GLib.UnhandledExceptionHandler(OnUnhandledException);

            StartupTrace("initializing GTK");
            Application.Init();

            var App = new Application("io.github.visageeee.forgeahead", GLib.ApplicationFlags.None);
            StartupTrace("registering GTK application");
            App.Register(GLib.Cancellable.Current);
            StartupTrace("GTK application registered");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ShapesFile shapes = new ShapesFile();
            StartupTrace("loading shapes");
            shapes.Load(Settings.GetSetting("ShapesFile/Path", ""));
            StartupTrace("shapes loaded");
            Shapes = shapes;
            StartupTrace("creating editor window");
            MapWindow window = new MapWindow("Forge Ahead");
            StartupTrace("editor window created");

            StartupTrace("opening map");
            if (args.Length == 1 && !args[0].StartsWith("-psn_"))
                window.OpenFile(args[0]);
            else {
                window.NewLevel();
                window.ShowStartup();
            }

            StartupTrace("running GTK event loop");
            Application.Run();
            return 0;
        }
    }
}
