using MicroserviceManager.Models;
using MicroserviceManager.Services;

namespace MicroserviceManager.UI
{
    public class SetupForm : Form
    {
        private TextBox txtEnvPath = new();
        private TextBox txtServicesPath = new();
        private Label lblStatus = new();
        private readonly SettingsManager _settingsManager = new();
        private UserSettings _settings;

        public SetupForm()
        {
            _settings = _settingsManager.LoadSettings();
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "Setup - Local Microservices Manager";
            Size = new Size(600, 320);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            Controls.Add(new Label { Text = "Welcome to Local Microservices Manager", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) });

            Controls.Add(new Label { Text = "Angular environment.ts file", Location = new Point(20, 60), AutoSize = true });
            txtEnvPath = new TextBox { Location = new Point(20, 80), Width = 450, ReadOnly = true, Text = _settings.EnvFilePath };
            var btnEnv = new Button { Text = "Browse", Location = new Point(480, 79), Width = 80 };
            btnEnv.Click += (s, e) => {
                using var dlg = new OpenFileDialog { Filter = "TypeScript files (*.ts)|*.ts|All files (*.*)|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK) txtEnvPath.Text = dlg.FileName;
            };
            Controls.Add(txtEnvPath); Controls.Add(btnEnv);

            Controls.Add(new Label { Text = "Microservices root folder", Location = new Point(20, 120), AutoSize = true });
            txtServicesPath = new TextBox { Location = new Point(20, 140), Width = 450, ReadOnly = true, Text = _settings.MicroservicesRootPath };
            var btnServices = new Button { Text = "Browse", Location = new Point(480, 139), Width = 80 };
            btnServices.Click += (s, e) => {
                using var dlg = new FolderBrowserDialog();
                if (dlg.ShowDialog() == DialogResult.OK) txtServicesPath.Text = dlg.SelectedPath;
            };
            Controls.Add(txtServicesPath); Controls.Add(btnServices);

            var btnVerify = new Button { Text = "VERIFY", Location = new Point(20, 190), Width = 540, Height = 40, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            btnVerify.Click += BtnVerify_Click;
            Controls.Add(btnVerify);

            lblStatus = new Label { Location = new Point(20, 240), AutoSize = true, ForeColor = Color.Red };
            Controls.Add(lblStatus);
        }

        private void BtnVerify_Click(object? sender, EventArgs e)
        {
            if (!File.Exists(txtEnvPath.Text)) { lblStatus.Text = "❌ Invalid environment.ts path."; return; }
            if (!Directory.Exists(txtServicesPath.Text)) { lblStatus.Text = "❌ Invalid microservices folder."; return; }

            _settings.EnvFilePath = txtEnvPath.Text;
            _settings.MicroservicesRootPath = txtServicesPath.Text;
            _settingsManager.SaveSettings(_settings);

            Hide();
            var mainForm = new MainForm(_settings);
            mainForm.FormClosed += (s, args) => Close();
            mainForm.Show();
        }
    }
}