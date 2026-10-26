using Microsoft.Win32;

namespace EngineeringQuantityAuditComparer.Tests;

public sealed class LegacyInstallerIdentityTests
{
    private const string Command = "rundll32.exe dfshim.dll,ShArpMaintain 工程量审减分析助手.app, Culture=neutral, PublicKeyToken=0000000000000000, processorArchitecture=msil";

    [Theory]
    [InlineData("内部使用", "工程量审减分析助手", Command, true)]
    [InlineData("yang123-pot", "工程量审减分析助手", Command, true)]
    [InlineData("其他发布者", "工程量审减分析助手", Command, false)]
    [InlineData("内部使用", "其他软件", Command, false)]
    [InlineData("内部使用", "工程量审减分析助手", "notepad.exe", false)]
    [InlineData("内部使用", "工程量审减分析助手", null, false)]
    public void AcceptsOnlyKnownPublishersWithVerifiedClickOnceIdentity(
        string publisher, string product, string? command, bool expected)
    {
        string path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexMigrationTests." + Guid.NewGuid().ToString("N");
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
            {
                key.SetValue("DisplayName", product);
                key.SetValue("Publisher", publisher);
                if (command is not null)
                    key.SetValue("UninstallString", command);
            }

            string? result = LegacyClickOnceMigration.LegacyInstallationIdentity.ReadVerifiedCommand(path);
            Assert.Equal(expected ? command : null, result);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }
}
