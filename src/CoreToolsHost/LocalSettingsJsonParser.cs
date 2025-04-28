// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace CoreToolsHost
{
    internal static class LocalSettingsJsonParser
    {
        internal static async Task<JsonDocument?> GetLocalSettingsJsonAsJObjectAsync()
        {
            var fullPath = Path.Combine(Environment.CurrentDirectory, "local.settings.json");

            if (File.Exists(fullPath))
            {
                string fileContent = string.Empty;
                using (var fileStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var streamReader = new StreamReader(fileStream))
                {
                    fileContent = await streamReader.ReadToEndAsync();
                }

                if (!string.IsNullOrEmpty(fileContent))
                {
                    var localSettingsJObject = JsonDocument.Parse(fileContent, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    return localSettingsJObject;
                }
            }

            return null;
        }
    }
}
