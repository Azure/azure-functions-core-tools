﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "publish", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Publish the current directory contents to an Azure Function App. Locally deleted files are not removed from destination.")]
    internal class PublishFunctionApp : BaseFunctionAppAction
    {
        private readonly ISettings _settings;
        private readonly ISecretsManager _secretsManager;
        private readonly IArmTokenManager _tokenManager;

        public bool PublishLocalSettings { get; set; }
        public bool OverwriteSettings { get; set; }
        public bool PublishLocalSettingsOnly { get; set; }

        public PublishFunctionApp(IArmManager armManager, ISettings settings, ISecretsManager secretsManager, IArmTokenManager tokenManager)
            : base(armManager)
        {
            _settings = settings;
            _secretsManager = secretsManager;
            _tokenManager = tokenManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>('i', "publish-local-settings")
                .WithDescription("Updates App Settings for the function app in Azure during deployment.")
                .Callback(f => PublishLocalSettings = f);
            Parser
                .Setup<bool>('o', "publish-settings-only")
                .WithDescription("Only publish settings and skip the content. Default is prompt.")
                .Callback(f => PublishLocalSettingsOnly = f);
            Parser
                .Setup<bool>('y', "overwrite-settings")
                .WithDescription("Only to be used in conjunction with -i or -o. Overwrites AppSettings in Azure with local value if different. Default is prompt.")
                .Callback(f => OverwriteSettings = f);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            ColoredConsole.WriteLine(WarningColor($"Publish {functionAppRoot} contents to an Azure Function App. Locally deleted files are not removed from destination."));
            ColoredConsole.WriteLine("Getting site publishing info...");
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            if (PublishLocalSettingsOnly)
            {
                var isSuccessful = await PublishAppSettings(functionApp);
                if (!isSuccessful)
                {
                    return;
                }
            }
            else
            {
                await RetryHelper.Retry(async () =>
                {
                    using (var client = await GetRemoteZipClient(new Uri($"https://{functionApp.ScmUri}")))
                    using (var request = new HttpRequestMessage(HttpMethod.Put, new Uri("api/zip/site/wwwroot", UriKind.Relative)))
                    {
                        request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);

                        ColoredConsole.WriteLine("Creating archive for current directory...");

                        request.Content = CreateZip(functionAppRoot);

                        ColoredConsole.WriteLine("Uploading archive...");
                        var response = await client.SendAsync(request);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new CliException($"Error uploading archive ({response.StatusCode}).");
                        }

                        response = await client.PostAsync("api/functions/synctriggers", content: null);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new CliException($"Error calling sync triggers ({response.StatusCode}).");
                        }

                        if (PublishLocalSettings)
                        {
                            var isSuccessful = await PublishAppSettings(functionApp);
                            if (!isSuccessful)
                            {
                                return;
                            }
                        }

                        ColoredConsole.WriteLine("Upload completed successfully.");
                    }
                }, 2);
            }
        }

        private async Task<bool> PublishAppSettings(Site functionApp)
        {
            var azureAppSettings = await _armManager.GetFunctionAppAppSettings(functionApp);
            var localAppSettings = _secretsManager.GetSecrets();
            var appSettings = MergeAppSettings(azureAppSettings, localAppSettings);
            var result = await _armManager.UpdateFunctionAppAppSettings(functionApp, appSettings);
            if (!result.IsSuccessful)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Error updating app settings:"))
                    .WriteLine(ErrorColor(result.ErrorResult));
                return false;
            }
            return true;
        }

        private IDictionary<string, string> MergeAppSettings(IDictionary<string, string> azure, IDictionary<string, string> local)
        {
            var result = new Dictionary<string, string>(azure);
            foreach (var pair in local)
            {
                if (result.ContainsKeyCaseInsensitive(pair.Key) &&
                    !result.GetValueCaseInsensitive(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    ColoredConsole.WriteLine($"App setting {pair.Key} is different between azure and {SecretsManager.AppSettingsFileName}");
                    if (OverwriteSettings)
                    {
                        ColoredConsole.WriteLine("Overwriting setting in azure with local value because '--overwrite-settings [-y]' was specified.");
                        result[pair.Key] = pair.Value;
                    }
                    else
                    {
                        var answer = string.Empty;
                        do
                        {
                            ColoredConsole.WriteLine(QuestionColor("Would you like to overwrite value in azure? [yes/no/show]"));
                            answer = Console.ReadLine();
                            if (answer.Equals("show", StringComparison.OrdinalIgnoreCase))
                            {
                                ColoredConsole
                                    .WriteLine($"Azure: {azure.GetValueCaseInsensitive(pair.Key)}")
                                    .WriteLine($"Locally: {pair.Value}");
                            }
                        } while (!answer.Equals("yes", StringComparison.OrdinalIgnoreCase) &&
                                 !answer.Equals("no", StringComparison.OrdinalIgnoreCase));

                        if (answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
                        {
                            result[pair.Key] = pair.Value;
                        }
                    }
                }
                else
                {
                    ColoredConsole.WriteLine($"Setting {pair.Key} = ****");
                    result[pair.Key] = pair.Value;
                }
            }

            return result;
        }

        private static StreamContent CreateZip(string path)
        {
            var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var fileName in FileSystemHelpers.GetFiles(path, new[] { ".git", ".vscode" }, new[] { ".gitignore", "appsettings.json", "local.settings.json", "project.lock.json" }))
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
                MaxResponseContentBufferSize = 30 * 1024 * 1024,
                Timeout = Timeout.InfiniteTimeSpan
            };
            var token = await _tokenManager.GetToken(_settings.CurrentTenant);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
}
