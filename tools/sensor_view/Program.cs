using System;
using System.Windows;

namespace SensorView
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var app = new Application();
            return app.Run(new MainWindow());
        }
    }
}
