using System;
using Microsoft.Win32;

namespace LegacyClickOnceMigration;

internal static class LegacyInstallationIdentity
{
    internal const string ProductName = "工程量审减分析助手";
    private const string CommandPrefix = "rundll32.exe dfshim.dll,ShArpMaintain 工程量审减分析助手.app,";

    internal static string? ReadVerifiedCommand(string registryPath)
    {
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath))
        {
            if (key == null ||
                !string.Equals(key.GetValue("DisplayName") as string, ProductName, StringComparison.Ordinal) ||
                !IsKnownPublisher(key.GetValue("Publisher") as string))
                return null;

            string? command = key.GetValue("UninstallString") as string;
            return command != null && command.StartsWith(CommandPrefix, StringComparison.Ordinal)
                ? command
                : null;
        }
    }

    private static bool IsKnownPublisher(string? publisher) =>
        string.Equals(publisher, "yang123-pot", StringComparison.Ordinal) ||
        string.Equals(publisher, "内部使用", StringComparison.Ordinal);
}
