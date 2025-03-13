using System.IO;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Azure.Functions.Cli.Common
{
    internal static class SelfHostWebHostSettingsFactory
    {
        public static ScriptApplicationHostOptions Create(string scriptPath)
        {
            return new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = scriptPath,
                LogPath = Path.Combine(Path.GetTempPath(), "LogFiles", "Application", "Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), "secrets", "functions", "secrets")
            };
        }
    }
}
