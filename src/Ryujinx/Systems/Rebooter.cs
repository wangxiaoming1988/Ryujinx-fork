using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Common.Locale;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Systems
{
    internal static class Rebooter
    {

        private static readonly string _updateDir = Path.Combine(Path.GetTempPath(), "Ryujinx", "update");

        public static void RebootAppWithGame(string gamePath, List<string> args)
        {
            _ = Reboot(gamePath, args);

        }

        private static async Task Reboot(string gamePath, List<string> args)
        {

            bool shouldRestart = true;

            TaskDialog taskDialog = new()
            {
                Header = LocaleManager.Instance[LocaleKeys.RyujinxRebooter],
                SubHeader = LocaleManager.Instance[LocaleKeys.DialogRebooterMessage],
                IconSource = new SymbolIconSource { Symbol = Symbol.Games },
                XamlRoot = RyujinxApp.MainWindow,
            };

            if (shouldRestart)
            {
                string executableDirectory = AppDomain.CurrentDomain.BaseDirectory;

                _ = taskDialog.ShowAsync(true);
                await Task.Delay(500);

                // Use the absolute executable path. A relative process name can resolve to
                // another Ryujinx installation or fail to preserve the app bundle on macOS.
                string executablePath = Environment.ProcessPath;

                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    string executableName = Path.GetFileName(executablePath);
                    executablePath = !string.IsNullOrEmpty(executableName)
                        ? Path.Combine(executableDirectory, executableName)
                        : Path.Combine(executableDirectory, OperatingSystem.IsWindows() ? "Ryujinx.exe" : "Ryujinx");
                }

                ProcessStartInfo processStart = new(executablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = executableDirectory,
                };

                foreach (string arg in args)
                {
                    processStart.ArgumentList.Add(arg);
                }

                processStart.ArgumentList.Add(gamePath);

                Process.Start(processStart);

                Environment.Exit(0);
            }
        }
    }
}
