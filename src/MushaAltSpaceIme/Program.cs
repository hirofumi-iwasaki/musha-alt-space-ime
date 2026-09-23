using System;
using System.Windows.Forms;

namespace MushaAltSpaceIme;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            MessageBox.Show(
                "このアプリは Windows 11 専用です。",
                "musha-alt-space-ime",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            using var singleInstance = new SingleInstanceGuard();
            if (!singleInstance.IsPrimaryInstance)
            {
                MessageBox.Show(
                    "musha-alt-space-ime はすでに起動しています。",
                    "musha-alt-space-ime",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using var trayContext = new TrayApplicationContext();
            Application.Run(trayContext);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"アプリを開始できませんでした。\n\n{exception.Message}",
                "musha-alt-space-ime",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
