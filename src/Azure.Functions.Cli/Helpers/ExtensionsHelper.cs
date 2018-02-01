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
                var assembly = typeof(ExtensionsHelper).Assembly;
                var extensionsProjText = string.Empty;
                using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".ExtensionsProj.txt"))
                using (var reader = new StreamReader(resource))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        extensionsProjText += $"{line}{Environment.NewLine}";
                    }
                }
                await FileSystemHelpers.WriteAllTextToFileAsync(extensionsProj, extensionsProjText);
            }
            return extensionsProj;
        }
    }
}
