using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Colors.Net;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using System.Linq;
using System.Reflection;

namespace Azure.Functions.Cli.Common
{
    internal class TemplatesManager : ITemplatesManager
    {
        private readonly ISecretsManager _secretsManager;

        public TemplatesManager(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public Task<IEnumerable<Template>> Templates
        {
            get
            {
                return GetTemplates();
            }
        }

        private static async Task<IEnumerable<Template>> GetTemplates()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
            string templatesJson; 

            if(extensionBundleManager.IsExtensionBundleConfigured())
            {
                var contentProvider = ExtensionBundleHelper.GetExtensionBundleContentProvider();
                templatesJson = await contentProvider.GetTemplates();
            }
            else
            {
                templatesJson = GetTemplatesJson();
            }

            var extensionBundleTemplates = JsonConvert.DeserializeObject<IEnumerable<Template>>(templatesJson);
            // Extension bundle versions are strings in the form <majorVersion>.<minorVersion>.<patchVersion>
            var extensionBundleMajorVersion = (await extensionBundleManager.GetExtensionBundleDetails()).Version[0];
            if (extensionBundleMajorVersion == '2' || extensionBundleMajorVersion == '3')
            {
                return extensionBundleTemplates.Concat(await GetStaticTemplates());
            }
            return extensionBundleTemplates;
        }

        // TODO: Remove this method once a solution for templates has been found
        private static async Task<IEnumerable<Template>> GetStaticTemplates()
        {
            var templatesList = new string[] {
                "BlobTrigger-Python-Preview-Append",
                "CosmosDBTrigger-Python-Preview-Append",
                "EventHubTrigger-Python-Preview-Append",
                "HttpTrigger-Python-Preview-Append",
                "QueueTrigger-Python-Preview-Append",
                "ServiceBusQueueTrigger-Python-Preview-Append",
                "ServiceBusTopicTrigger-Python-Preview-Append",
                "TimerTrigger-Python-Preview-Append"
            };

            IList<Template> templates = new List<Template>();
            foreach (var templateName in templatesList)
            {
                templates.Add(await CreateStaticTemplate(templateName));
            }
            return templates;
        }

        // TODO: Remove this hardcoding once a solution for templates has been found
        private static async Task<Template> CreateStaticTemplate(string templateName)
        {
            Template template = new Template();
            template.Id = templateName;
            template.Metadata = JsonConvert.DeserializeObject<TemplateMetadata>(
                await StaticResources.GetValue($"{templateName}.metadata.json"
            ));
            template.Files = new Dictionary<string, string> {
                { "function_app.py", await StaticResources.GetValue($"{templateName}.function_app.py") }
            };
            template.ProgrammingModel = ProgrammingModel.Preview;
            return template;
        }

        private static string GetTemplatesJson()
        {
            var templatesLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "templates", "templates.json");
            if (!FileSystemHelpers.FileExists(templatesLocation))
            {
                throw new CliException($"Can't find templates location. Looked at '{templatesLocation}'");
            }

            return FileSystemHelpers.ReadAllTextFromFile(templatesLocation);
        }

        public async Task Deploy(string Name, Template template, bool isNewProgrammingModel)
        {
            if (isNewProgrammingModel)
            {
                var fileName = ProgrammingModelHelper.GetNewProgrammingModelFunctionAppFileName(template.Metadata.Language);
                var path = Path.Join(Environment.CurrentDirectory, fileName);
                if (!FileSystemHelpers.FileExists(path))
                {
                    throw new CliException($"{fileName} was not found! This file is required for the new programming model for {template.Metadata.Language}");
                }
                
                ColoredConsole.WriteLine($"Writing {Name} to {path}");
                // Assume the function code is located in the file named after the main function app file for that programming model
                template.Files.TryGetValue(fileName, out string templateContent);
                templateContent = Regex.Replace(templateContent, template.Metadata.DefaultFunctionName, Name);
                // Add a new line before appending the function
                FileSystemHelpers.AppendAllTextToFile(path, Environment.NewLine + templateContent);                
            }
            else
            {
                var path = Path.Combine(Environment.CurrentDirectory, Name);
                if (FileSystemHelpers.DirectoryExists(path))
                {
                    var response = "n";
                    do
                    {
                        ColoredConsole.Write($"A directory with the name {Name} already exists. Overwrite [y/n]? [n] ");
                        response = Console.ReadLine();
                    } while (response != "n" && response != "y");
                    if (response == "n")
                    {
                        return;
                    }
                }

                if (FileSystemHelpers.DirectoryExists(path))
                {
                    FileSystemHelpers.DeleteDirectorySafe(path, ignoreErrors: false);
                }

                FileSystemHelpers.EnsureDirectory(path);

                foreach (var file in template.Files.Where(kv => !kv.Key.EndsWith(".dat")))
                {
                    var filePath = Path.Combine(path, file.Key);
                    ColoredConsole.WriteLine($"Writing {filePath}");
                    await FileSystemHelpers.WriteAllTextToFileAsync(filePath, file.Value);
                }
                var functionJsonPath = Path.Combine(path, "function.json");
                ColoredConsole.WriteLine($"Writing {functionJsonPath}");
                await FileSystemHelpers.WriteAllTextToFileAsync(functionJsonPath, JsonConvert.SerializeObject(template.Function, Formatting.Indented));
                if (template.Metadata.Extensions != null)
                {
                    foreach (var extension in template.Metadata.Extensions)
                    {
                        var installAction = new InstallExtensionAction(_secretsManager, false)
                        {
                            Package = extension.Id,
                            Version = extension.Version
                        };
                        await installAction.RunAsync();
                    }
                }
            }
        }
    }
}