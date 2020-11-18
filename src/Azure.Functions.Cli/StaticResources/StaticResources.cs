using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli
{
    public static class StaticResources
    {
        private static async Task<string> GetValue(string name)
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

        public static Task<string> DockerfileCustom => GetValue("Dockerfile.custom");

        public static Task<string> DockerfileCsxDotNet => GetValue("Dockerfile.csx.dotnet");

        public static Task<string> DockerfilePython36 => GetValue("Dockerfile.python36");

        public static Task<string> DockerfilePython37 => GetValue("Dockerfile.python37");

        public static Task<string> DockerfilePython38 => GetValue("Dockerfile.python38");

        public static Task<string> DockerfilePython39 => GetValue("Dockerfile.python39");

        public static Task<string> DockerfilePowershell => GetValue("Dockerfile.powershell");

        public static Task<string> DockerfileNode => GetValue("Dockerfile.node");

        public static Task<string> DockerIgnoreFile => GetValue("dockerignore");

        public static Task<string> VsCodeExtensionsJson => GetValue("vscode.extensions.json");

        public static Task<string> LocalSettingsJson => GetValue("local.settings.json");

        public static Task<string> HostJson => GetValue("host.json");

        public static Task<string> BundleConfig => GetValue("bundleConfig.json");

        public static Task<string> CustomHandlerConfig => GetValue("customHandlerConfig.json");

        public static Task<string> ManagedDependenciesConfig => GetValue("managedDependenciesConfig.json");

        public static Task<string> PythonDockerBuildScript => GetValue(Constants.StaticResourcesNames.PythonDockerBuild);

        public static Task<string> PowerShellProfilePs1 => GetValue("profile.ps1");

        public static Task<string> FuncIgnore => GetValue("funcignore");

        public static Task<string> PackageJson => GetValue("package.json");

        public static Task<string> JavascriptPackageJson => GetValue("javascriptPackage.json");

        public static Task<string> TsConfig => GetValue("tsconfig.json");

        public static Task<string> PowerShellRequirementsPsd1 => GetValue("requirements.psd1");

        public static Task<string> PythonRequirementsTxt => GetValue("requirements.txt");

        public static Task<string> PrintFunctionJson => GetValue("print-functions.sh");


        public static Task<string> KedaTemplate => GetValue("keda.yaml");

        public static Task<string> ZipToSquashfsScript => GetValue("ziptofs.sh");
    }
}
