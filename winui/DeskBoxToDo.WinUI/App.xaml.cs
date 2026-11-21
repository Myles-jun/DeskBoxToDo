using System;
using Microsoft.UI.Xaml;

namespace DeskBoxToDo.WinUI
{
    public partial class App : Application
    {
        private MainWindow _window;

        public App()
        {
            InitializeComponent();
            RequestedTheme = ApplicationTheme.Dark;
            UnhandledException += (sender, args) =>
            {
                Console.WriteLine("UNHANDLED: " + args.Exception);
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
