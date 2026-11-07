using MicroserviceManager.UI;

namespace MicroserviceManager
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new SetupForm());
        }
    }
}