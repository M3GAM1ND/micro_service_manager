using MicroserviceManager.Core;
using MicroserviceManager.UI;

namespace MicroserviceManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Two copies editing the same environment.ts would undo each other's changes.
        using var mutex = new Mutex(true, @"Local\MicroserviceManager.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Microservice Manager is already running.", "Microservice Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Workspace? workspace = null;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // Last resort: put files back before the process dies. Anything missed is restored on next start.
            try { workspace?.Shutdown(); } catch { }
            if (e.ExceptionObject is Exception ex) ReportCrash(ex);
        };

        try
        {
            var settings = SettingsStore.Load();
            if (!settings.PathsAreValid)
            {
                using var welcome = new SettingsForm(settings, firstRun: true);
                if (welcome.ShowDialog() != DialogResult.OK) return;
            }

            workspace = new Workspace(settings);
            using var main = new MainForm(workspace);
            Application.ThreadException += (_, e) => main.ShowError(e.Exception);
            Application.Run(main);
        }
        catch (Exception ex)
        {
            try { workspace?.Shutdown(); } catch { }
            ReportCrash(ex);
        }
        finally
        {
            workspace?.Dispose();
        }
    }

    /// <summary>Never fail silently: show what went wrong and keep a copy in crash.log.</summary>
    private static void ReportCrash(Exception ex)
    {
        string logPath = Path.Combine(AppPaths.DataFolder, "crash.log");
        try { File.WriteAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{ex}\r\n"); } catch { }
        MessageBox.Show(
            $"Microservice Manager couldn't continue:\n\n{ex.Message}\n\nDetails were saved to:\n{logPath}",
            "Microservice Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
