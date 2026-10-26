using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace LegacyClickOnceMigration;

internal static class Program
{
    private const string ProductName = "工程量审减分析助手";
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || !args[0].StartsWith(UninstallRoot, StringComparison.OrdinalIgnoreCase))
                return 2;

            string registryPath = args[0];
            string? command = LegacyInstallationIdentity.ReadVerifiedCommand(registryPath);
            if (command == null)
                return 3;

            var startInfo = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                command.Substring("rundll32.exe ".Length))
            {
                UseShellExecute = false,
                CreateNoWindow = false
            };
            using (Process? process = Process.Start(startInfo))
            {
                if (process == null)
                    return 4;
            }

            bool invokedRemove = false;
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (!RegistryKeyExists(registryPath))
                    return 0;

                if (!invokedRemove && TryConfirmRemove())
                    invokedRemove = true;

                Thread.Sleep(200);
            }

            return invokedRemove ? 5 : 6;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
            return 7;
        }
    }

    private static bool RegistryKeyExists(string registryPath)
    {
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath))
            return key != null;
    }

    private static bool TryConfirmRemove()
    {
        AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "UserInterfaceForm"));

        foreach (AutomationElement window in windows)
        {
            try
            {
                if (!window.Current.Name.StartsWith(ProductName, StringComparison.Ordinal))
                    continue;

                using (Process owner = Process.GetProcessById(window.Current.ProcessId))
                {
                    if (!string.Equals(owner.ProcessName, "dfsvc", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                AutomationElement? remove = window.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "radioRemove"));
                AutomationElement? ok = window.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "btnOk"));
                if (remove == null || ok == null)
                    continue;

                var selection = remove.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern;
                var invocation = ok.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                if (selection == null || invocation == null)
                    continue;

                selection.Select();
                invocation.Invoke();
                return true;
            }
            catch (ElementNotAvailableException)
            {
                // The ClickOnce window was closing while being inspected.
            }
            catch (InvalidOperationException)
            {
                // The ClickOnce controls are not ready yet; retry while the window exists.
            }
        }

        return false;
    }
}
