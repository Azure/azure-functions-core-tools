using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "publish", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Publish all of the current directory content to azure function app.")]
    internal class PublishFunctionApp : BaseFunctionAppAction
    {
        private readonly IArmManager _armManager;
        private readonly ISettings _settings;

        public PublishFunctionApp(IArmManager armManager, ISettings settings)
        {
            _armManager = armManager;
            _settings = settings;
        }

        public override async Task RunAsync()
        {
            ColoredConsole.WriteLine("Getting site publishing info...");
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            using (var client = await GetRemoteZipClient(new Uri($"https://{functionApp.ScmUri}")))
            using (var request = new HttpRequestMessage())
            {
                request.Method = HttpMethod.Put;
                request.RequestUri = new Uri("api/zip/site/wwwroot", UriKind.Relative);
                request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
                ColoredConsole.WriteLine("Creating archive for current directory...");
                request.Content = CreateZip(Environment.CurrentDirectory);
                ColoredConsole.WriteLine("Uploading archive...");
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                response = await client.PostAsync("api/functions/synctriggers", content: null);
                response.EnsureSuccessStatusCode();
                ColoredConsole.WriteLine("Upload completed successfully.");
            }
        }

        private static StreamContent CreateZip(string path)
        {
            var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var fileName in FileSystemHelpers.GetFiles(path, new[] { ".git", ".vscode" }, new[] { ".gitignore", "appsettings.json", "project.lock.json" }))
                {
                    zip.AddFile(fileName, fileName, path);
                }
            }
            memoryStream.Seek(0, SeekOrigin.Begin);
            var content = new StreamContent(memoryStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return content;
        }

        private async Task<HttpClient> GetRemoteZipClient(Uri url)
        {
            var client = new HttpClient
            {
                BaseAddress = url,
                MaxResponseContentBufferSize = 30 * 1024 * 1024
            };

            client.DefaultRequestHeaders.Authorization = await _armManager.GetAuthenticationHeader(_settings.CurrentSubscription);
            return client;
        }
    }
}
