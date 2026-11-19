using System;
using System.Threading;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Some gateways still negotiate TLS 1.0 by default on .NET Framework.
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |=
                    System.Net.SecurityProtocolType.Tls12;
            }
            catch
            {
                // Older runtime: leave the default alone.
            }

            if (args.Length > 0)
            {
                string first = args[0].ToLowerInvariant();
                bool cliInvocation =
                    first == "--text" ||
                    first == "--parse" ||
                    first == "--help" ||
                    first == "-h" ||
                    first == "--version" ||
                    first == "--writetest" ||
                    first.StartsWith("--", StringComparison.Ordinal);

                if (cliInvocation)
                {
                    {
                    var cliConfig = AppConfig.Load();
                    Ui.RadiusOverride = cliConfig.CornerRadius;
                    return Cli.Run(args);
                }
                }
            }

            using (var mutex = new Mutex(true, @"Local\DeskBoxToDo_SingleInstance", out bool created))
            {
                if (!created)
                {
                    // Already running: ask the live instance to show its window,
                    // otherwise double-clicking the exe looks like nothing happened.
                    try
                    {
                        using (EventWaitHandle signal = EventWaitHandle.OpenExisting(TrayShell.ActivationEventName))
                        {
                            signal.Set();
                        }
                    }
                    catch
                    {
                        // Instance may be starting up; ignore.
                    }

                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var startupConfig = AppConfig.Load();
            Ui.RadiusOverride = startupConfig.CornerRadius;

            using (var shell = new TrayShell())
                {
                    Application.Run(shell);
                }

                GC.KeepAlive(mutex);
            }

            return 0;
        }
    }
}
