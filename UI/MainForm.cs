using System.ComponentModel;
using System.Diagnostics;
using MicroserviceManager.Models;
using MicroserviceManager.Services;

namespace MicroserviceManager.UI
{
    public class MainForm : Form
    {
        private readonly UserSettings _settings;
        private readonly SettingsManager _settingsManager = new();
        private readonly MicroserviceScanner _scanner = new();
        private readonly ProcessManager _processManager = new();
        private readonly DatabaseManager _databaseManager = new();
        private EnvironmentManager _envManager;
        private BindingList<Microservice> _services = new();

        private CheckedListBox clbServices = new();
        private Label lblStatus = new();
        private RadioButton rbQa = new();
        private RadioButton rbUat = new();
        private TextBox txtDatabase = new();
        private ContextMenuStrip _contextMenu = new();

        public MainForm(UserSettings settings)
        {
            _settings = settings;
            _envManager = new EnvironmentManager(_settings.EnvFilePath);
            InitializeUI();
            LoadServices();
        }

        private void InitializeUI()
        {
            Text = "Local Microservices Manager";
            Size = new Size(800, 720);
            StartPosition = FormStartPosition.CenterScreen;

            Controls.Add(new Label { Text = "LOCAL MICROSERVICES", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) });

            Controls.Add(new Label { Text = "Unchecked Services Target:", Location = new Point(20, 62), AutoSize = true });
            rbQa = new RadioButton { Text = "QA", Location = new Point(220, 60), AutoSize = true };
            rbUat = new RadioButton { Text = "UAT", Location = new Point(280, 60), AutoSize = true };

            if (_settings.RemoteEnvironment == "UAT") rbUat.Checked = true;
            else rbQa.Checked = true;
            Controls.Add(rbQa); Controls.Add(rbUat);

            Controls.Add(new Label { Text = "Target Database Name:", Location = new Point(20, 95), AutoSize = true });
            txtDatabase = new TextBox { Location = new Point(220, 92), Width = 300, Text = _settings.DatabaseName };
            Controls.Add(txtDatabase);

            // Tip Label for Visual Studio
            Controls.Add(new Label { Text = "💡 Tip: Right-click a service to open it in Visual Studio", Location = new Point(20, 120), AutoSize = true, ForeColor = Color.DimGray });

            clbServices = new CheckedListBox { Location = new Point(20, 140), Size = new Size(740, 360), Font = new Font("Consolas", 10) };
            clbServices.ItemCheck += ClbServices_ItemCheck;

            // Setup Context Menu for Visual Studio
            var openVsItem = new ToolStripMenuItem("Open in Visual Studio 2026");
            openVsItem.Click += OpenVsItem_Click;
            _contextMenu.Items.Add(openVsItem);
            clbServices.ContextMenuStrip = _contextMenu;
            clbServices.MouseDown += ClbServices_MouseDown;

            Controls.Add(clbServices);

            var btnApplyRun = new Button { Text = "▶ APPLY & RUN", Location = new Point(20, 520), Size = new Size(150, 40), BackColor = Color.LightGreen };
            btnApplyRun.Click += BtnApplyRun_Click;
            Controls.Add(btnApplyRun);

            var btnStopAll = new Button { Text = "■ STOP ALL", Location = new Point(190, 520), Size = new Size(120, 40), BackColor = Color.LightCoral };
            btnStopAll.Click += BtnStopAll_Click;
            Controls.Add(btnStopAll);

            lblStatus = new Label { Location = new Point(20, 580), Size = new Size(740, 80), Font = new Font("Segoe UI", 9) };
            Controls.Add(lblStatus);

            FormClosing += MainForm_FormClosing;
        }

        private void LoadServices()
        {
            lblStatus.Text = "Scanning...";
            var found = _scanner.ScanFolder(_settings.MicroservicesRootPath);

            _databaseManager.EnsureBackups(found);

            _services.Clear();
            clbServices.Items.Clear();

            foreach (var svc in found)
            {
                _services.Add(svc);
                UpdateListboxItem(svc);
            }

            lblStatus.Text = $"✓ {found.Count} microservices discovered and backed up safely.";
        }

        private void UpdateListboxItem(Microservice svc)
        {
            string remoteEnv = rbUat.Checked ? "UAT" : "QA";
            string statusTag = svc.IsLocalSelected ? "LOCAL" : remoteEnv;
            string displayText = $"{svc.Name,-20} | {svc.EnvVariableName,-20} | [{statusTag}] | {svc.Status}";

            int index = _services.IndexOf(svc);
            if (clbServices.Items.Count > index)
            {
                clbServices.Items[index] = displayText;
                clbServices.SetItemChecked(index, svc.IsLocalSelected);
            }
            else clbServices.Items.Add(displayText, svc.IsLocalSelected);
        }

        private void ClbServices_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            var svc = _services[e.Index];
            svc.IsLocalSelected = e.NewValue == CheckState.Checked;
            BeginInvoke(new Action(() => UpdateListboxItem(svc)));
        }

        // Detect right-clicks to select the specific service
        private void ClbServices_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = clbServices.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches) clbServices.SelectedIndex = index;
            }
        }

        // Handle opening in Visual Studio
        private void OpenVsItem_Click(object? sender, EventArgs e)
        {
            if (clbServices.SelectedIndex == -1) return;
            var svc = _services[clbServices.SelectedIndex];
            string selectedEnv = rbUat.Checked ? "UAT" : "QA";

            _settings.DatabaseName = txtDatabase.Text;
            _settings.RemoteEnvironment = selectedEnv;
            _settingsManager.SaveSettings(_settings);

            _processManager.StopService(svc); // Stop CMD if it's already running

            svc.OpenedInVs = true;
            svc.IsLocalSelected = true;
            svc.Status = "● VISUAL STUDIO";

            _databaseManager.UpdateConnectionString(svc, txtDatabase.Text);
            _envManager.ApplyChanges(_services.ToList(), selectedEnv);

            try
            {
                // This utilizes the Windows Default Program association for .csproj files
                Process.Start(new ProcessStartInfo { FileName = svc.ProjectFilePath, UseShellExecute = true });
                lblStatus.Text = $"✓ Opened {svc.Name} in Visual Studio. Database and environment updated.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"❌ Failed to open Visual Studio: {ex.Message}";
            }

            UpdateListboxItem(svc);
        }

        private void BtnApplyRun_Click(object? sender, EventArgs e)
        {
            int started = 0, stopped = 0;
            string selectedEnv = rbUat.Checked ? "UAT" : "QA";

            _settings.DatabaseName = txtDatabase.Text;
            _settings.RemoteEnvironment = selectedEnv;
            _settingsManager.SaveSettings(_settings);

            foreach (var svc in _services)
            {
                if (svc.IsLocalSelected)
                {
                    _databaseManager.UpdateConnectionString(svc, txtDatabase.Text);

                    // Skip starting CMD if the user is debugging it in Visual Studio
                    if (!svc.OpenedInVs && (svc.RunningProcess == null || svc.RunningProcess.HasExited))
                    {
                        _processManager.StartService(svc);
                        started++;
                    }
                }
                else
                {
                    svc.OpenedInVs = false; // Reset VS flag if they uncheck it
                    _databaseManager.RestoreOriginal(svc);

                    if (svc.RunningProcess != null && !svc.RunningProcess.HasExited)
                    {
                        _processManager.StopService(svc);
                        stopped++;
                    }
                    else if (svc.Status == "● VISUAL STUDIO")
                    {
                        svc.Status = "○ STOPPED";
                    }
                }
                UpdateListboxItem(svc);
            }

            _envManager.ApplyChanges(_services.ToList(), selectedEnv);
            lblStatus.Text = $"✓ Applied. {started} started in CMD, {stopped} stopped/reverted.";
        }

        private void BtnStopAll_Click(object? sender, EventArgs e)
        {
            string selectedEnv = rbUat.Checked ? "UAT" : "QA";

            foreach (var svc in _services)
            {
                _processManager.StopService(svc);
                _databaseManager.RestoreOriginal(svc);
                svc.IsLocalSelected = false;
                svc.OpenedInVs = false;
                if (svc.Status == "● VISUAL STUDIO") svc.Status = "○ STOPPED";
                UpdateListboxItem(svc);
            }

            _envManager.ApplyChanges(_services.ToList(), selectedEnv);
            lblStatus.Text = $"All services stopped.\n✓ environment.ts and all appsettings.json files reverted to normal.";
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            string selectedEnv = rbUat.Checked ? "UAT" : "QA";

            foreach (var svc in _services)
            {
                _processManager.StopService(svc);
                _databaseManager.RestoreOriginal(svc);
            }

            _envManager.ApplyChanges(new List<Microservice>(), selectedEnv);
        }
    }
}