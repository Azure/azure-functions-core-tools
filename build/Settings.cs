using System.Collections.Generic;
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

        public const string DotnetIsolatedItemTemplatesVersion = "4.0.2288";
        public const string DotnetIsolatedProjectTemplatesVersion = "4.0.2288";
        public const string DotnetItemTemplatesVersion = "4.0.2185";
        public const string DotnetProjectTemplatesVersion = "4.0.2185";
        public const string TemplateJsonVersion = "3.1.1648";

        public static readonly string SBOMManifestToolPath = Path.GetFullPath("../ManifestTool/Microsoft.ManifestTool.dll");

        public static readonly string SrcProjectPath = Path.GetFullPath("../src/Azure.Functions.Cli/");

        public static readonly string ConstantsFile = Path.Combine(SrcProjectPath, "Common", "Constants.cs");

        public static readonly string TestProjectPath = Path.GetFullPath("../test/Azure.Functions.Cli.Tests/");

        public static readonly string ProjectFile = Path.Combine(SrcProjectPath, "Azure.Functions.Cli.csproj");

        public static readonly string TestProjectFile = Path.Combine(TestProjectPath, "Azure.Functions.Cli.Tests.csproj");

        public static readonly string DurableFolder = Path.Combine(TestProjectPath, "Resources", "DurableTestFolder");

        public static readonly string[] TargetRuntimes = new[] {
            "min.win-arm64",
            "min.win-x86",
            "min.win-x64",
            "linux-x64",
            "osx-x64",
            "osx-arm64",
            "win-x86",
            "win-x64",
            "win-arm64" };

        public static readonly Dictionary<string, string> RuntimesToOS = new Dictionary<string, string>
        {
            { "win-x86", "WINDOWS" },
            { "win-x64", "WINDOWS" },
            { "win-arm64", "WINDOWS"},
            { "linux-x64", "LINUX" },
            { "osx-x64", "OSX" },
            { "osx-arm64", "OSX" },
            { "min.win-x86", "WINDOWS" },
            { "min.win-x64", "WINDOWS" },
            { "min.win-arm64", "WINDOWS"},
        };

        private static readonly string[] _winPowershellRuntimes = new[]
        {
            "win-x86",
            "win",
            "win10-x86",
            "win8-x86",
            "win81-x86",
            "win7-x86",
            "win-arm64",
            "win-x64",
            "win10-x64",
            "win8-x64",
            "win81-x64",
            "win7-x64"
        };

        private static readonly string[] _linPowershellRuntimes = new[]
        {
            "linux",
            "linux-x64",
            "unix",
            "linux-musl-x64"
        };

        private static readonly string[] _osxPowershellRuntimes = new[]
        {
            "osx",
            "osx-x64",
            "unix"
        };

        private static readonly string[] _osxARMPowershellRuntimes = new[]
        {
            "osx",
            "osx-arm64",
            "unix"
        };

        private static Dictionary<string, string[]> GetPowerShellRuntimes()
        {
            var runtimes = new Dictionary<string, string[]>
            {
                { "win-x86", _winPowershellRuntimes },
                { "win-x64", _winPowershellRuntimes },
                { "win-arm64", _winPowershellRuntimes },
                { "linux-x64", _linPowershellRuntimes },
                { "osx-x64", _osxPowershellRuntimes },
                { "osx-arm64", _osxARMPowershellRuntimes }
            };

            return runtimes;
        }

        public static readonly Dictionary<string, Dictionary<string, string[]>> ToolsRuntimeToPowershellRuntimes = new Dictionary<string, Dictionary<string, string[]>>
        {
            {
                "7",
                new Dictionary<string, string[]>
                {
                    { "win-x86", _winPowershellRuntimes },
                    { "win-x64", _winPowershellRuntimes },
                    { "win-arm64", _winPowershellRuntimes },
                    { "linux-x64", _linPowershellRuntimes },
                    { "osx-x64", _osxPowershellRuntimes },
                    // NOTE: PowerShell 7.0 does not support arm. First version supporting it is 7.2
                    // https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-macos?view=powershell-7.2#supported-versions
                    // That being said, we might as well include "osx" and "unix" since it'll hardly affect package size and should lead to more accurate error messages
                    { "osx-arm64", new [] { "osx", "unix" } }
                }
            },
            {
                "7.2",
                GetPowerShellRuntimes()
            },
            {
                "7.4",
                GetPowerShellRuntimes()
            }
        };

        public static readonly string[] LanguageWorkers = new[] { "java", "powershell", "node", "python" };

        public static string MinifiedVersionPrefix = "min.";

        public const string DistLibVersion = "distlib-0.3.0";

        public const string DistLibUrl = "https://github.com/vsajip/distlib/archive/0.3.0.zip";

        public static readonly string OutputDir = Path.Combine(Path.GetFullPath(".."), "artifacts");

        public static readonly string SBOMManifestTelemetryDir = Path.Combine(OutputDir, "SBOMManifestTelemetry");
        
        public static string TargetFramework = "net6.0";

        public static readonly string NupkgPublishDir = Path.GetFullPath($"../src/Azure.Functions.Cli/bin/Release/{TargetFramework}/publish");
        
        public static readonly string PreSignTestDir = "PreSignTest";

        public static readonly string SignTestDir = "SignTest";
       
        public static readonly string DotnetIsolatedItemTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ItemTemplates/{DotnetIsolatedItemTemplatesVersion}";

        public static readonly string DotnetIsolatedProjectTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ProjectTemplates/{DotnetIsolatedProjectTemplatesVersion}";

        public static readonly string DotnetItemTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ItemTemplates/{DotnetItemTemplatesVersion}";

        public static readonly string DotnetProjectTemplates = $"https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ProjectTemplates/{DotnetProjectTemplatesVersion}";

        public static readonly string TemplatesJsonZip = $"https://functionscdn.azureedge.net/public/TemplatesApi/{TemplateJsonVersion}.zip";

        public static readonly string TelemetryKeyToReplace = "00000000-0000-0000-0000-000000000000";

        public static string BuildNumber => config("9999", "devops_buildNumber");

        public static string IntegrationBuildNumber => config(null, "integrationBuildNumber") ?? string.Empty;

        public static string CommitId => config("N/A", "Build.SourceVersion");

        public static string TelemetryInstrumentationKey => config(null, "TELEMETRY_INSTRUMENTATION_KEY");

        public static string BuildArtifactsStorage => config(null);

        public class SignInfo
        {
            public static string AzureSigningConnectionString => config(null, "AzureBlobSigningConnectionString");
            public static readonly string AzureToSignContainerName = "azure-functions-cli";
            public static readonly string AzureSignedContainerName = "azure-functions-cli-signed";
            public static readonly string AzureSigningJobName = "signing-jobs";
            public static readonly string ToSignZipName = $"{BuildNumber}-authenticode.zip";
            public static readonly string ToSignThirdPartyName = $"{BuildNumber}-third.zip";
            public static readonly string ToSignDir = "ToSign";
            public static readonly string SignedDir = "signed";
            public static readonly string Authenticode = "SignAuthenticode";
            public static readonly string ToAuthenticodeSign = "Authenticode";
            public static readonly string ThirdParty = "Sign3rdParty";
            public static readonly string ToThirdPartySign = "ThirdParty";
            public static readonly string ToMacSign = "Mac";
            public static readonly string[] RuntimesToSign = new string[] { "min.win-arm64", "min.win-x86", "min.win-x64", "osx-arm64", "osx-x64" };
            public static readonly string[] FilterExtensionsSign = new[] { ".json", ".spec", ".cfg", ".pdb", ".config", ".nupkg", ".py", ".md" };
            public static readonly string SigcheckDownloadURL = "https://functionsbay.blob.core.windows.net/public/tools/sigcheck64.exe";

            public static readonly string[] SkipSigcheckTest = new[] {
                "aspnetcorev2_inprocess.dll",
                "Microsoft.AspNetCore.Buffering.dll",
                "Microsoft.AspNetCore.Server.IIS.dll"
            };

            public static readonly string[] macBinaries = new[] {
                "func",
                "gozip"
            };

            public static readonly string[] authentiCodeBinaries = new[] {
                "DurableTask.AzureStorage.Internal.dll",
                "DurableTask.Core.Internal.dll",
                "func.dll",
                "func.exe",
                "gozip.exe",
                "func.pdb",
                "Microsoft.Azure.AppService.*",
                "Microsoft.Azure.WebJobs.*",
                "Microsoft.Azure.WebSites.DataProtection.dll",
                "Azure.Core.dll",
                "Azure.Identity.dll",
                "Azure.Storage.Blobs.dll",
                "Azure.Storage.Common.dll",
                "Microsoft.Extensions.Azure.dll",
                "Microsoft.Identity.Client.dll",
                "Microsoft.Identity.Client.Extensions.Msal.dll",
                Path.Combine("workers", "python")
            };

            public static readonly string[] thirdPartyBinaries = new[] {
                "AccentedCommandLineParser.dll",
                "Autofac.dll",
                "Azure.Security.KeyVault.*",
                "BouncyCastle.Crypto.dll",
                "Colors.Net.dll",
                "DotNetZip.dll",
                "Dynamitey.dll",
                "Google.Protobuf.dll",
                "Grpc.AspNetCore.Server.ClientFactory.dll",
                "Grpc.AspNetCore.Server.dll",
                "Grpc.Core.dll",
                "Grpc.Core.Api.dll",
                "Grpc.Net.Client.dll", 
                "Grpc.Net.ClientFactory.dll",
                "Grpc.Net.Common.dll",
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
                "dotnet-aspnet-codegenerator-design.dll",
                "DotNetTI.BreakingChangeAnalysis.dll",
                "Microsoft.Azure.KeyVault.*",
                "Microsoft.AI.*.dll",
                "Microsoft.Build.Framework.dll",
                "Microsoft.Build.dll",
                "Microsoft.CodeAnalysis.dll",
                "Microsoft.CodeAnalysis.CSharp.dll",
                "Microsoft.CodeAnalysis.CSharp.Scripting.dll",
                "Microsoft.CodeAnalysis.CSharp.Workspaces.dll",
                "Microsoft.CodeAnalysis.Razor.dll",
                "Microsoft.CodeAnalysis.Scripting.dll",
                "Microsoft.CodeAnalysis.VisualBasic.dll",
                "Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll",
                "Microsoft.CodeAnalysis.Workspaces.dll",
                "Microsoft.DotNet.PlatformAbstractions.dll",
                "Microsoft.Extensions.DependencyModel.dll",
                "Microsoft.Extensions.DiagnosticAdapter.dll",
                "Microsoft.Extensions.Logging.ApplicationInsights.dll",
                "Microsoft.Extensions.PlatformAbstractions.dll",
                "Microsoft.Azure.Services.AppAuthentication.dll",
                "Microsoft.IdentityModel.*",
                "Microsoft.ApplicationInsights.*",
                "Microsoft.Rest.ClientRuntime.*",
                "Microsoft.VisualStudio.Web.CodeGenera*",
                "Microsoft.WindowsAzure.Storage.dll",
                "Microsoft.AspNetCore.*",
                "NuGet.*.dll",
                "protobuf-net.Core.dll",
                "System.Composition.*",
                "System.Configuration.ConfigurationManager.dll",
                "System.Data.SqlClient.dll",
                "System.Diagnostics.PerformanceCounter.dll",
                "System.IdentityModel.Tokens.Jwt.dll",
                "System.Interactive.Async.dll",
                "System.Memory.Data.dll",
                "System.Net.Http.Formatting.dll",
                "System.Private.ServiceModel.dll",
                "System.Reactive.*.dll",
                "System.Security.Cryptography.ProtectedData.dll",
                "YamlDotNet.dll",
                "Marklio.Metadata.dll",
                "Microsoft.Azure.Cosmos.Table.dll",
                "Microsoft.Azure.DocumentDB.Core.dll",
                "Microsoft.Azure.Storage.Blob.dll",
                "Microsoft.Azure.Storage.Common.dll",
                "Microsoft.Azure.Storage.File.dll",
                "Microsoft.Azure.Storage.Queue.dll",
                "Microsoft.OData.Core.dll",
                "Microsoft.OData.Edm.dll",
                "Microsoft.Spatial.dll",
                "Mono.Posix.NETStandard.dll",
                Path.Combine("tools", "python", "packapp", "distlib")
            };
        }
    }
}
