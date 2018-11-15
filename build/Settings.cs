using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Build
{
    public static class Settings
    {
        static Settings()
        {
            Directory.CreateDirectory(OutputDir);
        }

        private static string config(string @default = null, [CallerMemberName] string key = null)
        {
            var value = System.Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(value)
                ? @default
                : value;
        }

        public const string ItemTemplatesVersion = "2.0.10321";
        public const string ProjectTemplatesVersion = "2.0.10321";

        public static readonly string SrcProjectPath = Path.GetFullPath("../src/Azure.Functions.Cli/");

        public static readonly string TestProjectPath = Path.GetFullPath("../test/Azure.Functions.Cli.Tests/");

        public static readonly string ProjectFile = Path.Combine(SrcProjectPath, "Azure.Functions.Cli.csproj");

        public static readonly string TestProjectFile = Path.Combine(TestProjectPath, "Azure.Functions.Cli.Tests.csproj");

        public static readonly string DurableFolder = Path.Combine(TestProjectPath, "Resources", "DurableTestFolder");

        public static readonly string ConstantsFile = Path.Combine(SrcProjectPath, "Common", "Constants.cs");

        public static readonly string[] TargetRuntimes = new[] { "win-x86", "win-x64", "linux-x64", "osx-x64", "no-runtime" };

        public const string DistLibVersion = "distlib-15dba58a827f56195b0fa0afe80a8925a92e8bf5";

        public const string DistLibUrl = "https://github.com/vsajip/distlib/archive/15dba58a827f56195b0fa0afe80a8925a92e8bf5.zip";

        public const string PythonWorkerUrl = "https://raw.githubusercontent.com/Azure/azure-functions-python-worker/1.0.0a6/python/worker.py";

        public const string PythonWorkerConfigUrl = "https://raw.githubusercontent.com/Azure/azure-functions-python-worker/1.0.0a6/python/worker.config.json";

        public static readonly string OutputDir = Path.Combine(Path.GetFullPath(".."), "artifacts");

        public static readonly string ItemTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ItemTemplates/{ItemTemplatesVersion}";

        public static readonly string ProjectTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ProjectTemplates/{ProjectTemplatesVersion}";

        public static readonly string TelemetryKeyToReplace = "00000000-0000-0000-0000-000000000000";

        public static string BuildNumber => config(null, "Build.BuildId") ?? config("9999", "APPVEYOR_BUILD_NUMBER");

        public static string CommitId => config(null, "Build.SourceVersion") ?? config("N /A", "APPVEYOR_REPO_COMMIT");

        public static string TelemetryInstrumentationKey => config(null, "TELEMETRY_INSTRUMENTATION_KEY");

        public static string BuildArtifactsStorage => config(null);
    }
}