using System.Threading;
using System.Windows.Forms;

namespace AcHudLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, "Local\\ACDisplayUnit.Singleton", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "AC Display Unit is already running.",
                "AC Display Unit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}
