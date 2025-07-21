// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http.Handlers;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public class PublishHelper
    {
        public static GitIgnoreParser GetIgnoreParser(string workingDir)
        {
            try
            {
                var path = Path.Combine(workingDir, Constants.FuncIgnoreFile);
                if (FileSystemHelpers.FileExists(path))
                {
                    return new GitIgnoreParser(FileSystemHelpers.ReadAllTextFromFile(path));
                }
            }
            catch
            {
            }

            return null;
        }

        public static async Task<HttpResponseMessage> InvokeLongRunningRequest(HttpClient client, ProgressMessageHandler handler, HttpRequestMessage request, long requestSize = 0, string prompt = null)
        {
            if (prompt == null)
            {
                prompt = string.Empty;
            }

            string message = string.Empty;
            if (requestSize > 0)
            {
                message = Utilities.BytesToHumanReadable(requestSize);
            }

            using (var pb = new SimpleProgressBar($"{prompt} {message}"))
            {
                handler.HttpSendProgress += (s, e) => pb.Report(e.ProgressPercentage);
                return await client.SendAsync(request);
            }
        }

        public static async Task CheckResponseStatusAsync(HttpResponseMessage response, string message = null)
        {
            if (message == null)
            {
                message = string.Empty;
            }

            if (response == null)
            {
                throw new ArgumentNullException("Response must not be null");
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var errorMessage = $"Error {message} ({response.StatusCode}).";

                if (!string.IsNullOrEmpty(responseContent))
                {
                    errorMessage += $"{Environment.NewLine}Server Response: {responseContent}";
                }

                throw new CliException(errorMessage);
            }
        }

        public static bool IsLinuxFxVersionUsingCustomImage(string linuxFxVersion)
        {
            if (string.IsNullOrEmpty(linuxFxVersion))
            {
                return false;
            }

            bool isStartingWithDocker = linuxFxVersion.StartsWith("docker|", StringComparison.OrdinalIgnoreCase);
            bool isLegacyImageMatched = Constants.WorkerRuntimeImages.Values
                .SelectMany(image => image)
                .Any(image => linuxFxVersion.Contains(image, StringComparison.OrdinalIgnoreCase));

            return isStartingWithDocker && !isLegacyImageMatched;
        }

        public static bool IsLinuxFxVersionRuntimeMatched(string linuxFxVersion, WorkerRuntime runtime)
        {
            if (string.IsNullOrEmpty(linuxFxVersion))
            {
                // Suppress the check since when LinuxFxVersion == "", runtime image will depends on FUNCTIONS_WORKER_RUNTIME setting
                return true;
            }

            // Test if linux fx version matches any legacy runtime image (e.g. DOCKER|mcr.microsoft.com/azure-functions/dotnet)
            bool isStartingWithDocker = linuxFxVersion.StartsWith("docker|", StringComparison.OrdinalIgnoreCase);
            bool isLegacyImageMatched = false;
            if (Constants.WorkerRuntimeImages.TryGetValue(runtime, out IEnumerable<string> legacyImages))
            {
                isLegacyImageMatched = legacyImages
                    .Any(image => linuxFxVersion.Contains(image, StringComparison.OrdinalIgnoreCase));
            }

            // Test if linux fx version matches any official runtime image (e.g. DOTNET, DOTNET|2)
            bool isOfficialImageMatched = linuxFxVersion.StartsWith(runtime.ToString(), StringComparison.OrdinalIgnoreCase);

            return isOfficialImageMatched || (isStartingWithDocker && isLegacyImageMatched);
        }
    }
}
