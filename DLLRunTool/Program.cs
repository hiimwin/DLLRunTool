using DLLRunTool;
using System;
using System.Windows.Forms;

namespace DLLRunTool
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
