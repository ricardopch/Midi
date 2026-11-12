using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Melodía MIDI")]
[assembly: AssemblyDescription("Pasa a notas una canción y crea un MP4 tocado con un instrumento y su archivo MIDI")]
[assembly: AssemblyProduct("Melodía MIDI")]
[assembly: AssemblyCompany("ricardopch")]
[assembly: AssemblyCopyright("ricardopch 2026")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: ComVisible(false)]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.5", FrameworkDisplayName = ".NET Framework 4.5")]

namespace MelodiaMidi
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            if (AudioIO.IsWindows)
            {
                try { SetProcessDPIAware(); } catch (Exception) { }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { ShowError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) ShowError(ex);
            };
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }

        private static void ShowError(Exception e)
        {
            MessageBox.Show("Ha ocurrido un error inesperado:\n\n" + e.Message, "Melodía MIDI",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
