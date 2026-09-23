using System;
using System.IO;
using Microsoft.Win32;

namespace MushaAltSpaceIme;

internal sealed class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "MushaAltSpaceIme";

    public bool IsRegistered()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return runKey?.GetValue(ValueName) is string command &&
               string.Equals(command, GetCommand(), StringComparison.Ordinal);
    }

    public void SetRegistered(bool enabled)
    {
        if (enabled)
        {
            var command = GetCommand();
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("自動起動の登録先を開けませんでした。");
            runKey.SetValue(ValueName, command, RegistryValueKind.String);

            if (runKey.GetValue(ValueName) is not string saved ||
                !string.Equals(saved, command, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("自動起動の登録を確認できませんでした。");
            }

            return;
        }

        using (var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        if (IsRegistered())
        {
            throw new InvalidOperationException("自動起動の解除を確認できませんでした。");
        }
    }

    // StartupApproved is owned by Windows.  We only inspect it, never write it.
    public bool IsDisabledByWindowsStartupSettings()
    {
        using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: false);
        return approvedKey?.GetValue(ValueName) is byte[] value && value.Length > 0 && value[0] == 0x03;
    }

    private static string GetCommand()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("実行ファイルの場所を取得できませんでした。");
        }

        return $"\"{Path.GetFullPath(executablePath)}\"";
    }
}
