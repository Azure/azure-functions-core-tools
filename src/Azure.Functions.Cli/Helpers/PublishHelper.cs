using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Handlers;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
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
            catch { }
            return null;
        }

        public static BuildOption ResolveBuildOption(BuildOption currentBuildOption, WorkerRuntime runtime, Site site, bool buildNativeDeps, bool noBuild)
        {
            // --no-build and --build-native-deps will take precedence over --build local and --build remote
            if (noBuild)
            {
                return BuildOption.None;
            }

            if (buildNativeDeps)
            {
                return BuildOption.Container;
            }

            if (currentBuildOption == BuildOption.Default)
            {
                // Change to remote build if, python app, has requirements.txt, requirements.txt has content
                if (runtime == WorkerRuntime.python &&
                    FileSystemHelpers.FileExists(Constants.RequirementsTxt) &&
                    new FileInfo(Path.Combine(Environment.CurrentDirectory, Constants.RequirementsTxt)).Length > 0)
                {
                    return BuildOption.Remote;
                }
            }
            return currentBuildOption;
        }

        public static async Task<HttpResponseMessage> InvokeLongRunningRequest(HttpClient client,
            ProgressMessageHandler handler, HttpRequestMessage request, long requestSize = 0, string prompt = null)
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
    }
}