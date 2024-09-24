using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FunctionsCustomHost
{
    internal static class LocalSettingsJsonParser
    {
        internal static async Task<JsonDocument> GetLocalSettingsJsonAsJObjectAsync()
        {
            var fullPath = Path.Combine(Environment.CurrentDirectory, "local.settings.json");

            if (File.Exists(fullPath))
            {
                string fileContent = "";
                using (var fileStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var streamReader = new StreamReader(fileStream))
                {
                    fileContent = await streamReader.ReadToEndAsync();
                }
                if (!string.IsNullOrEmpty(fileContent))
                {
                    var localSettingsJObject = JsonDocument.Parse(fileContent);
                    return localSettingsJObject;
                }
            }

            return null;
        }
    }
}
