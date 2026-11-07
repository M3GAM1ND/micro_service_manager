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

        var settings = SettingsStore.Load();
        if (!settings.PathsAreValid)
        {
            using var welcome = new SettingsForm(settings, firstRun: true);
            if (welcome.ShowDialog() != DialogResult.OK) return;
        }

        using var workspace = new Workspace(settings);
        using var main = new MainForm(workspace);

        Application.ThreadException += (_, e) => main.ShowError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            // Last resort: put files back before the process dies. Anything missed is restored on next start.
            try { workspace.Shutdown(); } catch { }
        };

        Application.Run(main);
    }
}
