using Azure.Functions.Cli.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Script.Description;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Helpers
{
    class ExtensionsHelper
    {
        public static async Task<string> EnsureExtensionsProjectExistsAsync(ISecretsManager secretsManager, bool csx, string extensionsDir = null)
        {
            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet && !csx)
            {
                return DotnetHelpers.GetCsprojOrFsproj();
            }

            if (String.IsNullOrEmpty(extensionsDir))
            {
                extensionsDir = Environment.CurrentDirectory;
            }

            var extensionsProj = Path.Combine(extensionsDir, Constants.ExtensionsCsProjFile);
            if (!FileSystemHelpers.FileExists(extensionsProj))
            {
                FileSystemHelpers.EnsureDirectory(extensionsDir);
                await FileSystemHelpers.WriteAllTextToFileAsync(extensionsProj, await StaticResources.ExtensionsProject);
            }
            return extensionsProj;
        }

        private static IEnumerable<string> GetBindings()
        {
            var functionJsonfiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: Constants.FunctionJsonFileName);
            var bindings = new HashSet<string>();
            foreach (var functionJson in functionJsonfiles)
            {
                string functionJsonContents = FileSystemHelpers.ReadAllTextFromFile(functionJson);
                var functionMetadata = JsonConvert.DeserializeObject<FunctionMetadata>(functionJsonContents);
                foreach (var binding in functionMetadata.Bindings)
                {
                    bindings.Add(binding.Type.ToLower());
                }
            }
            return bindings;
        }

        public static string GetScriptFilePath()
        {
            // Ignore current implementation, I need help getting the script file path.

            // Below should be the logic to get the script file path...

            // If we don't have functions, this mean we don't have function.json
            // If we don't have a function.json file, we can't determine the script file
            var scriptFile =
                FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: Constants.FunctionJsonFileName)
                    .Select(p =>
                    {
                        var functionJsonFileContent = FileSystemHelpers.ReadAllTextFromFile(p);
                        var functionMetadata = JsonConvert.DeserializeObject<FunctionMetadata>(functionJsonFileContent);
                        return functionMetadata.ScriptFile[1..];
                    }).FirstOrDefault();
            return scriptFile;
        }

        public static IEnumerable<ExtensionPackage> GetExtensionPackages()
        {
            Dictionary<string, ExtensionPackage> packages = new Dictionary<string, ExtensionPackage>();
            foreach (var binding in GetBindings())
            {
                if (Constants.BindingPackageMap.TryGetValue(binding, out ExtensionPackage package))
                {
                    packages.TryAdd(package.Name, package);
                }
            }

            // We only need extensionsMetadatGeneratorPackage, if there is a binding that requires an extension
            // So, if we didn't find any extension packages, we don't need to add this.
            if (packages.Count != 0)
            {
                packages.Add("ExtensionsMetadataGeneratorPackage", Constants.ExtensionsMetadataGeneratorPackage);
            }

            return packages.Values;
        }

    }
}
