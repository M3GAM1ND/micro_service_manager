using System.Diagnostics;

namespace MicroserviceManager.Models
{
    public class UserSettings
    {
        public string EnvFilePath { get; set; } = string.Empty;
        public string MicroservicesRootPath { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "Fre8_Database";
        public string RemoteEnvironment { get; set; } = "QA";
    }

    public class Microservice
    {
        public string Name { get; set; } = string.Empty;
        public string ProjectFilePath { get; set; } = string.Empty;
        public string LocalUrl { get; set; } = string.Empty;
        public string EnvVariableName { get; set; } = string.Empty;
        public bool IsLocalSelected { get; set; }
        public bool OpenedInVs { get; set; } // NEW: Tracks if it's running in Visual Studio
        public string Status { get; set; } = "○ STOPPED";
        public Process? RunningProcess { get; set; }
    }
}