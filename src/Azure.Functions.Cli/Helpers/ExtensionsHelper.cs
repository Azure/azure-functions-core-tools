using Azure.Functions.Cli.Common;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    class ExtensionsHelper
    {
        public static async Task<string> EnsureExtensionsProjectExistsAsync()
        {
            var extensionsDir = Path.Combine(Environment.CurrentDirectory, "functions-extensions");
            var extensionsProj = Path.Combine(extensionsDir, "extensions.csproj");
            if (!FileSystemHelpers.FileExists(extensionsProj))
            {
                FileSystemHelpers.EnsureDirectory(extensionsDir);
                await FileSystemHelpers.WriteAllTextToFileAsync(extensionsProj, await StaticResources.ExtensionsProject);
            }
            return extensionsProj;
        }
    }
}
