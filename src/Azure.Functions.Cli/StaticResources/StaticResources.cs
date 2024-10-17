using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli
{
    public static class StaticResources
    {
        public static async Task<string> GetValue(string name)
        {
            var assembly = typeof(StaticResources).Assembly;
            var resourceName = $"{assembly.GetName().Name}.{name}";
            using (var stream = typeof(StaticResources).Assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                var sb = new StringBuilder();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    sb.AppendFormat("{0}{1}", line, reader.EndOfStream ? string.Empty : Environment.NewLine);
                }
                return sb.ToString();
            }
        }

        public static Task<string> ExtensionsProject => GetValue("ExtensionsProj.csproj");

        public static Task<string> GitIgnore => GetValue("gitignore");

        public static Task<string> DockerfileDotNet => GetValue("Dockerfile.dotnet");
        
        public static Task<string> DockerfileDotNet8 => GetValue("Dockerfile.dotnet8");

        public static Task<string> DockerfileCustom => GetValue("Dockerfile.custom");

        public static Task<string> DockerfileCsxDotNet => GetValue("Dockerfile.csx.dotnet");

        public static Task<string> DockerfileDotnetIsolated => GetValue("Dockerfile.dotnetIsolated");
        public static Task<string> DockerfileDotnet7Isolated => GetValue("Dockerfile.dotnet7Isolated");
        public static Task<string> DockerfileDotnet8Isolated => GetValue("Dockerfile.dotnet8Isolated");
        public static Task<string> DockerfileDotnet9Isolated => GetValue("Dockerfile.dotnet9Isolated");

        public static Task<string> DockerfileJava8 => GetValue("Dockerfile.java8");

        public static Task<string> DockerfileJava11 => GetValue("Dockerfile.java11");

        public static Task<string> DockerfilePython37 => GetValue("Dockerfile.python3.7");
        
        public static Task<string> DockerfilePython38 => GetValue("Dockerfile.python3.8");

        public static Task<string> DockerfilePython39 => GetValue("Dockerfile.python3.9");
        
        public static Task<string> DockerfilePython310 => GetValue("Dockerfile.python3.10");

        public static Task<string> DockerfilePython311 => GetValue("Dockerfile.python3.11");

        public static Task<string> DockerfilePowershell7 => GetValue("Dockerfile.powershell7");

        public static Task<string> DockerfilePowershell72 => GetValue("Dockerfile.powershell7.2");

        public static Task<string> DockerfileJavaScript => GetValue("Dockerfile.javascript");

        public static Task<string> DockerfileTypeScript => GetValue("Dockerfile.typescript");

        public static Task<string> DockerIgnoreFile => GetValue("dockerignore");

        public static Task<string> VsCodeExtensionsJson => GetValue("vscode.extensions.json");

        public static Task<string> LocalSettingsJson => GetValue("local.settings.json");

        public static Task<string> HostJson => GetValue("host.json");

        public static Task<string> BundleConfig => GetValue("bundleConfig.json");
        
        public static Task<string> BundleConfigPyStein => GetValue("bundleConfigPyStein.json");

        public static Task<string> BundleConfigNodeV4 => GetValue("bundleConfigNodeV4.json");


        public static Task<string> CustomHandlerConfig => GetValue("customHandlerConfig.json");

        public static Task<string> ManagedDependenciesConfig => GetValue("managedDependenciesConfig.json");

        public static Task<string> PythonDockerBuildScript => GetValue(Constants.StaticResourcesNames.PythonDockerBuild);

        public static Task<string> PowerShellProfilePs1 => GetValue("profile.ps1");

        public static Task<string> FuncIgnore => GetValue("funcignore");

        public static Task<string> PackageJsonJsV4 => GetValue("package-js-v4.json");

        public static Task<string> PackageJsonJs => GetValue("package-js.json");

        public static Task<string> PackageJsonTsV4 => GetValue("package-ts-v4.json");

        public static Task<string> PackageJsonTs => GetValue("package-ts.json");

        public static Task<string> TsConfig => GetValue("tsconfig.json");

        public static Task<string> PowerShellRequirementsPsd1 => GetValue("requirements.psd1");

        public static Task<string> PythonRequirementsTxt => GetValue("requirements.txt");

        public static Task<string> PythonGettingStartedMarkdown => GetValue("getting_started_python_function.md");

        public static Task<string> PrintFunctionJson => GetValue("print-functions.sh");


        public static Task<string> KedaV1Template => GetValue("keda-v1.yaml");
        public static Task<string> KedaV2Template => GetValue("keda-v2.yaml");

        public static Task<string> ZipToSquashfsScript => GetValue("ziptofs.sh");

        public static Task<string> StacksJson => GetValue("stacks.json");
    }
}
