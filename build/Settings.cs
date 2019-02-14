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

        public const string ItemTemplatesVersion = "2.0.10328";
        public const string ProjectTemplatesVersion = "2.0.10328";

        public static readonly string SrcProjectPath = Path.GetFullPath("../src/Azure.Functions.Cli/");

        public static readonly string TestProjectPath = Path.GetFullPath("../test/Azure.Functions.Cli.Tests/");

        public static readonly string ProjectFile = Path.Combine(SrcProjectPath, "Azure.Functions.Cli.csproj");

        public static readonly string TestProjectFile = Path.Combine(TestProjectPath, "Azure.Functions.Cli.Tests.csproj");

        public static readonly string DurableFolder = Path.Combine(TestProjectPath, "Resources", "DurableTestFolder");

        public static readonly string[] TargetRuntimes = new[] { "win-x86", "win-x64", "linux-x64", "osx-x64", "no-runtime", "min.win-x86", "min.win-x64" };

        public static readonly string[] LanguageWorkers = new[] { "Java", "Powershell", "Node" };

        public static string MinifiedVersionPrefix = "min.";

        public const string DistLibVersion = "distlib-15dba58a827f56195b0fa0afe80a8925a92e8bf5";

        public const string DistLibUrl = "https://github.com/vsajip/distlib/archive/15dba58a827f56195b0fa0afe80a8925a92e8bf5.zip";

        public const string PythonWorkerUrl = "https://raw.githubusercontent.com/Azure/azure-functions-python-worker/1.0.0b3/python/worker.py";

        public const string PythonWorkerConfigUrl = "https://raw.githubusercontent.com/Azure/azure-functions-python-worker/1.0.0b3/python/worker.config.json";

        public static readonly string OutputDir = Path.Combine(Path.GetFullPath(".."), "artifacts");

        public static readonly string ItemTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ItemTemplates/{ItemTemplatesVersion}";

        public static readonly string ProjectTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ProjectTemplates/{ProjectTemplatesVersion}";

        public static string BuildNumber => config(null, "Build.BuildId") ?? config("9999", "APPVEYOR_BUILD_NUMBER");

        public static string CommitId => config(null, "Build.SourceVersion") ?? config("N /A", "APPVEYOR_REPO_COMMIT");

        public static string BuildArtifactsStorage => config(null);

        public class SignInfo
        {
            public static string AzureSigningConnectionString => config(null, "AzureBlobSigningConnectionString");
            public static readonly string AzureToSignContainerName = "azure-functions-cli";
            public static readonly string AzureSignedContainerName = "azure-functions-cli-signed";
            public static readonly string AzureSigningJobName = "signing-jobs";
            public static readonly string ToSignZipName = $"{BuildNumber}-authenticode.zip";
            public static readonly string ToSignThirdPartyName = $"{BuildNumber}-third.zip";
            public static readonly string ToSignDir = "unsigned";
            public static readonly string SignedDir = "signed";
            public static readonly string Authenticode = "SignAuthenticode";
            public static readonly string ThirdParty = "Sign3rdParty";
            public static readonly string[] RuntimesToSign = new[] {"min.win-x86"};
            public static readonly string[] filterExtenstionsSign = new[] { ".json", ".spec", ".cfg", ".pdb", ".config", ".nupkg", ".py" };

            public static readonly string[] authentiCodeBinaries = new[] {
                "DurableTask.AzureStorage.Internal.dll",
                "DurableTask.Core.Internal.dll",
                "func.dll",
                "func.exe",
                "func.pdb",
                "Microsoft.Azure.AppService.Proxy.Client.dll",
                "Microsoft.Azure.AppService.Proxy.Common.dll",
                "Microsoft.Azure.AppService.Proxy.Runtime.dll",
                "Microsoft.Azure.WebJobs.dll",
                "Microsoft.Azure.WebJobs.Extensions.Http.dll",
                "Microsoft.Azure.WebJobs.Host.dll",
                "Microsoft.Azure.WebJobs.Host.Storage.dll",
                "Microsoft.Azure.WebJobs.Logging.ApplicationInsights.dll",
                "Microsoft.Azure.WebJobs.Logging.dll",
                "Microsoft.Azure.WebJobs.Script.dll",
                "Microsoft.Azure.WebJobs.Script.Grpc.dll",
                "Microsoft.Azure.WebJobs.Script.WebHost.dll",
                "Microsoft.Azure.WebSites.DataProtection.dll",
                Path.Combine("templates", "itemTemplates.2.0.10328.nupkg"),
                Path.Combine("templates", "projectTemplates.2.0.10328.nupkg"),
                Path.Combine("tools", "python", "packapp", "__main__.py"),
                Path.Combine("workers", "python")
            };

            public static readonly string[] thirdPartyBinaries = new[] {
                "AccentedCommandLineParser.dll",
                "Autofac.dll",
                "BouncyCastle.Crypto.dll",
                "Colors.Net.dll",
                "Dynamitey.dll",
                "Google.Protobuf.dll",
                "Grpc.Core.dll",
                "grpc_csharp_ext.x64.dll",
                "grpc_csharp_ext.x86.dll",
                "HTTPlease.Core.dll",
                "HTTPlease.Diagnostics.dll",
                "HTTPlease.Formatters.dll",
                "HTTPlease.Formatters.Json.dll",
                "ImpromptuInterface.dll",
                "KubeClient.dll",
                "KubeClient.Extensions.KubeConfig.dll",
                "NCrontab.Signed.dll",
                "Newtonsoft.Json.Bson.dll",
                "Newtonsoft.Json.dll",
                "protobuf-net.dll",
                "Remotion.Linq.dll",
                "System.IO.Abstractions.dll",
                "YamlDotNet.dll",
                Path.Combine("tools", "python", "packapp", "distlib")
            };
        }
    }
}