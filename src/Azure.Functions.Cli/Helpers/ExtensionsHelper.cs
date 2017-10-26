using Azure.Functions.Cli.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    class ExtensionsHelper
    {
        public static async Task<string> EnsureExtensionsProjectExistsAsync()
        {
            var extensionsProj = Path.Combine(Environment.CurrentDirectory, "extensions.csproj");
            if (!FileSystemHelpers.FileExists(extensionsProj))
            {
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
