using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MicroserviceManager.Models;

namespace MicroserviceManager.Services
{
    public static class EnvironmentTemplates
    {
        public const string QA = @"export const environment = {
    production: true,
    ApplicationName: 'FRE8',
    WorkingEnvironment: 'qa',
    API: 'https://fre8tmsqa.com/frqa/OrganizationMasterService',
    LegalEntityAPI: 'https://fre8tmsqa.com/frqa/LegalEntityService',
    SharedServiceAPI: 'https://fre8tmsqa.com/frqa/SharedService',
    MasterDataAPI: 'https://fre8tmsqa.com/frqa/MasterDataService',
    freightLogo: 'http://52.235.39.94/fre8/assets/images/logos/Fre8-removebg-preview.png',
    LoginAPI: 'https://fre8tmsqa.com/frqa/LoginService',
    Dispatch: 'https://fre8tmsqa.com/frqa/DispatchService',
    PayrollAPI: 'https://fre8tmsqa.com/frqa/PayrollService',
    InterComapanyAPI: 'https://fre8tmsqa.com/frqa/InterCompanyService',
    ReportsAPI: 'https://fre8tmsqa.com/frqa/ReportsService',
    GeneralAccountsAPI: 'https://fre8tmsqa.com/frqa/GeneralAccountsService',
    SignalrAPI: 'https://qasignalr-edamg9azf8a7ajgr.canadacentral-01.azurewebsites.net/api?code=PASTE_KEY_HERE',
    DriverApp: 'https://fre8tmsqa.com/frqa/DriverAppService',
    newTabInitialUrl:'/frqa/fre8',
    applicationLink: 'https://fre8tmsqa.com/frqa/fre8/',
    documentFolder: 'C:\\inetpub\\wwwroot\\fre8tmsQA\\DocumentUploaderAPI_QA\\Image',
    FleetMaintenanceAndSafetyAPI: 'https://fre8tmsqa.com/frqa/FleetMaintenanceService',
    defaultUserRoleName: ""Fre8 Admin"",
    DocumentAPI: 'https://fre8tmsqa.com/DocumentUploaderAPI_QA',
    SalesAPI: 'https://fre8tmsqa.com/frqa/SalesService',
    SignalRConnectionResetTime: 60000,
};";

        public const string UAT = @"export const environment = {
    production: true,
    ApplicationName: 'FRE8 UAT3',
    WorkingEnvironment: 'UAT',
    newTabInitialUrl:'/uat3/home',
    defaultUserRoleName: ""Fre8 Admin"",
    API: 'https://fre8tmsqa.com/uat3/OrganizationMasterService',
    LegalEntityAPI: 'https://fre8tmsqa.com/uat3/LegalEntityService',
    SharedServiceAPI: 'https://fre8tmsqa.com/uat3/SharedService',
    MasterDataAPI: 'https://fre8tmsqa.com/uat3/MasterDataService',
    freightLogo: 'https://fre8tmsqa.com/uat3/fre8/assets/images/logos/Fre8-removebg-preview.png',
    LoginAPI: 'https://fre8tmsqa.com/uat3/LoginService',
    Dispatch: 'https://fre8tmsqa.com/uat3/DispatchService',
    PayrollAPI: 'https://fre8tmsqa.com/uat3/PayrollService',
    FleetMaintenanceAndSafetyAPI: 'https://fre8tmsqa.com/uat3/FleetMaintenanceService',
    InterComapanyAPI: 'https://fre8tmsqa.com/uat3/InterCompanyService',
    ReportsAPI: 'https://fre8tmsqa.com/uat3/ReportsService',
    GeneralAccountsAPI: 'https://fre8tmsqa.com/uat3/GeneralAccountsService',
    SignalrAPI: 'https://fre8tmsqa.com/uat3/SignalRService',
    DriverApp: 'https://fre8tmsqa.com/uat3/DriverAppService',
    SalesAPI: 'https://fre8tmsqa.com/uat3/SalesService',
    DocumentAPI: 'https://fre8tmsqa.com/DocumentUploaderAPI_QA',
    applicationLink: 'https://fre8tmsqa.com/uat3/home/',
    documentFolder: 'C:\\inetpub\\wwwroot\\fre8tmsQA\\DocumentUploaderAPI_QA\\Image',
    SignalRConnectionResetTime: 60000,
};";
    }

    public class SettingsManager
    {
        private readonly string _settingsFilePath;

        public SettingsManager()
        {
            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicroserviceManager");
            if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);
            _settingsFilePath = Path.Combine(appFolder, "settings.json");
        }

        public UserSettings LoadSettings()
        {
            if (!File.Exists(_settingsFilePath)) return new UserSettings();
            try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_settingsFilePath)) ?? new UserSettings(); }
            catch { return new UserSettings(); }
        }

        public void SaveSettings(UserSettings settings) =>
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public class DatabaseManager
    {
        private readonly string _backupFolder;

        public DatabaseManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _backupFolder = Path.Combine(appData, "MicroserviceManager", "Backups", "AppSettings");
            if (!Directory.Exists(_backupFolder)) Directory.CreateDirectory(_backupFolder);
        }

        public void EnsureBackups(List<Microservice> services)
        {
            foreach (var svc in services)
            {
                string projectDir = Path.GetDirectoryName(svc.ProjectFilePath)!;
                string[] settingsFiles = { "appsettings.json", "appsettings.Development.json" };

                foreach (var file in settingsFiles)
                {
                    string sourcePath = Path.Combine(projectDir, file);
                    string backupPath = Path.Combine(_backupFolder, $"{svc.Name}_{file}");

                    if (File.Exists(sourcePath) && !File.Exists(backupPath))
                    {
                        File.Copy(sourcePath, backupPath);
                    }
                }
            }
        }

        public void UpdateConnectionString(Microservice svc, string newDbName)
        {
            if (string.IsNullOrWhiteSpace(newDbName)) return;

            string projectDir = Path.GetDirectoryName(svc.ProjectFilePath)!;
            string[] settingsFiles = { "appsettings.json", "appsettings.Development.json" };

            foreach (var file in settingsFiles)
            {
                string filePath = Path.Combine(projectDir, file);
                if (File.Exists(filePath))
                {
                    string content = File.ReadAllText(filePath);
                    string updatedContent = Regex.Replace(
                        content,
                        @"(?i)(Database|Initial\s+Catalog)\s*=\s*([^;""]+)",
                        $"$1={newDbName}"
                    );

                    if (content != updatedContent) File.WriteAllText(filePath, updatedContent);
                }
            }
        }

        public void RestoreOriginal(Microservice svc)
        {
            string projectDir = Path.GetDirectoryName(svc.ProjectFilePath)!;
            string[] settingsFiles = { "appsettings.json", "appsettings.Development.json" };

            foreach (var file in settingsFiles)
            {
                string sourcePath = Path.Combine(projectDir, file);
                string backupPath = Path.Combine(_backupFolder, $"{svc.Name}_{file}");

                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, sourcePath, true);
                }
            }
        }
    }

    public class MicroserviceScanner
    {
        public List<Microservice> ScanFolder(string rootFolder)
        {
            var services = new List<Microservice>();
            if (!Directory.Exists(rootFolder)) return services;

            string[] csprojFiles = Directory.GetFiles(rootFolder, "*.csproj", SearchOption.AllDirectories);

            foreach (string file in csprojFiles)
            {
                string projectName = Path.GetFileNameWithoutExtension(file);

                if (projectName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                    projectName.Contains("Entities", StringComparison.OrdinalIgnoreCase) ||
                    projectName.Contains("DAL", StringComparison.OrdinalIgnoreCase) ||
                    projectName.Contains("BL", StringComparison.OrdinalIgnoreCase) ||
                    projectName.Contains("Database", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? localUrl = GetLocalUrl(file);
                if (!string.IsNullOrEmpty(localUrl))
                {
                    services.Add(new Microservice
                    {
                        Name = projectName,
                        ProjectFilePath = file,
                        LocalUrl = localUrl,
                        EnvVariableName = DetermineEnvVariable(projectName)
                    });
                }
            }
            return services.OrderBy(s => s.Name).ToList();
        }

        private string DetermineEnvVariable(string projectName)
        {
            if (projectName.Equals("OrganizationMaster", StringComparison.OrdinalIgnoreCase)) return "API";
            if (projectName.Equals("InterCompany", StringComparison.OrdinalIgnoreCase)) return "InterComapanyAPI";
            if (projectName.Equals("Dispatch", StringComparison.OrdinalIgnoreCase)) return "Dispatch";
            if (projectName.Equals("DriverApp", StringComparison.OrdinalIgnoreCase)) return "DriverApp";
            return projectName.EndsWith("API", StringComparison.OrdinalIgnoreCase) ? projectName : $"{projectName}API";
        }

        private string? GetLocalUrl(string csprojPath)
        {
            try
            {
                string launchSettingsPath = Path.Combine(Path.GetDirectoryName(csprojPath)!, "Properties", "launchSettings.json");
                if (!File.Exists(launchSettingsPath)) return null;

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
                if (doc.RootElement.TryGetProperty("profiles", out JsonElement profiles))
                {
                    foreach (JsonProperty profile in profiles.EnumerateObject())
                    {
                        if (profile.Value.TryGetProperty("commandName", out JsonElement cmd) && cmd.GetString() == "Project")
                        {
                            if (profile.Value.TryGetProperty("applicationUrl", out JsonElement url))
                            {
                                string[] urls = (url.GetString() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
                                return urls.FirstOrDefault(u => u.StartsWith("http://")) ?? urls.FirstOrDefault();
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    public class ProcessManager
    {
        public void StartService(Microservice service)
        {
            if (service.RunningProcess != null && !service.RunningProcess.HasExited) return;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k title {service.Name} & echo Starting {service.Name}... & dotnet run",
                WorkingDirectory = Path.GetDirectoryName(service.ProjectFilePath)!,
                UseShellExecute = true
            };

            service.RunningProcess = Process.Start(startInfo);
            service.Status = "● RUNNING";
        }

        public void StopService(Microservice service)
        {
            if (service.RunningProcess == null || service.RunningProcess.HasExited)
            {
                service.Status = "○ STOPPED";
                return;
            }

            try { service.RunningProcess.Kill(true); }
            catch { }
            finally
            {
                service.RunningProcess = null;
                service.Status = "○ STOPPED";
            }
        }
    }

    public class EnvironmentManager
    {
        private readonly string _envPath;

        public EnvironmentManager(string envPath)
        {
            _envPath = envPath;
        }

        public void ApplyChanges(List<Microservice> services, string targetRemoteEnvironment)
        {
            if (!File.Exists(_envPath)) return;

            string content = targetRemoteEnvironment == "UAT" ? EnvironmentTemplates.UAT : EnvironmentTemplates.QA;

            foreach (var svc in services)
            {
                if (!svc.IsLocalSelected) continue;

                content = Regex.Replace(
                    content,
                    $@"\b({svc.EnvVariableName}\s*:\s*['""])([^'""]+)(['""])",
                    $"${{1}}{svc.LocalUrl}${{3}}"
                );
            }

            File.WriteAllText(_envPath, content);
        }
    }
}
