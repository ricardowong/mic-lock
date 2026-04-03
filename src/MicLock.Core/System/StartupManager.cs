using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MicLock.Core.System;

[SupportedOSPlatform("windows")]
public class StartupManager
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string DefaultAppName = "MicLock";
    public const string DefaultArguments = "--minimized";

    private static readonly byte[] StartupEnabledBytes = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public virtual bool IsRegisteredForStartup(string appName = DefaultAppName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            var value = key?.GetValue(appName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public virtual bool SetStartup(bool enable, string? executablePath = null, string appName = DefaultAppName, string arguments = DefaultArguments)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string targetExe = executablePath ?? Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(targetExe)) return false;

                // Format command line with quotes and arguments
                string command = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{targetExe}\""
                    : $"\"{targetExe}\" {arguments}";

                key.SetValue(appName, command);

                // Ensure Windows Task Manager lists it explicitly as Enabled
                try
                {
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true)
                                         ?? Registry.CurrentUser.CreateSubKey(StartupApprovedKey, writable: true);
                    approvedKey?.SetValue(appName, StartupEnabledBytes, RegistryValueKind.Binary);
                }
                catch { }
            }
            else
            {
                if (key.GetValue(appName) != null)
                {
                    key.DeleteValue(appName, throwOnMissingValue: false);
                }

                try
                {
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
                    if (approvedKey?.GetValue(appName) != null)
                    {
                        approvedKey.DeleteValue(appName, throwOnMissingValue: false);
                    }
                }
                catch { }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public virtual string? GetStartupCommand(string appName = DefaultAppName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            return key?.GetValue(appName) as string;
        }
        catch
        {
            return null;
        }
    }
}
