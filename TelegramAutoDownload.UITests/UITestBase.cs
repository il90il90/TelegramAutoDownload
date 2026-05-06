using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace TelegramAutoDownload.UITests
{
    /// <summary>
    /// Custom [Fact] attribute that automatically skips the test when the environment
    /// variable SKIP_UI_TESTS=1 is set (e.g. in headless CI environments).
    /// </summary>
    public sealed class UIFact : FactAttribute
    {
        public UIFact()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("SKIP_UI_TESTS"), "1",
                    StringComparison.OrdinalIgnoreCase))
                Skip = "SKIP_UI_TESTS=1 — UI automation tests require a Windows display";
        }
    }

    /// <summary>Collection definition that serialises UI tests so only one app runs at a time.</summary>
    [CollectionDefinition("UI", DisableParallelization = true)]
    public class UICollection { }


    /// <summary>
    /// Base class for all UI automation tests.
    /// Launches the WPF application before each test class and disposes it afterwards.
    ///
    /// Prerequisites:
    ///   1. Build the WPF project in Release (or Debug) before running UI tests.
    ///   2. The executable must exist at the path returned by <see cref="AppExePath"/>.
    ///   3. These tests require a Windows display — set SKIP_UI_TESTS=1 to bypass in headless CI.
    /// </summary>
    public abstract class UITestBase : IDisposable
    {
        protected Application App { get; }
        protected UIA3Automation Automation { get; }
        protected Window MainWindow { get; }

        protected UITestBase()
        {
            Automation = new UIA3Automation();
            App = Application.Launch(AppExePath());

            // Give the application time to initialise its main window
            MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            try { App.Close(); } catch { }
            App.Dispose();
            Automation.Dispose();
        }

        /// <summary>
        /// Returns the path to the compiled WPF executable.
        /// Looks for the Debug build first, then Release.
        /// Adjust <see cref="ExeName"/> if the output file is named differently.
        /// </summary>
        private static string AppExePath()
        {
            const string ExeName = "TelegramAutoDownload.exe";
            var root = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                             "..", "..", "..", "..",  // UITests/bin/Debug/net8.0-windows → solution root
                             "TelegramAutoDownload", "bin"));

            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(root, config, "net8.0-windows", ExeName);
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException(
                $"Could not find {ExeName}. Build the TelegramAutoDownload project first.", ExeName);
        }
    }
}
