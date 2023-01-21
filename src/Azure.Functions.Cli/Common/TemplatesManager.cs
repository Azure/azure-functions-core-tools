using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Colors.Net;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.ExtensionBundle;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Routing.Constraints;

namespace Azure.Functions.Cli.Common
{
    internal class TemplatesManager : ITemplatesManager
    {
        private const string PythonProgrammingModelMainFileKey = "function_app.py";
        private const string PythonProgrammingModelNewFileKey = "function_new_app.py";
        private const string PythonProgrammingModelMainNewFileKey = "function_app_new.py";
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

            if (false) // temporary: For testing. (extensionBundleManager.IsExtensionBundleConfigured())
            {
                await ExtensionBundleHelper.GetExtensionBundle();
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

        private static string GetTemplatesJson()
        {
            var templatesLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "templates", "templates.json");
            if (!FileSystemHelpers.FileExists(templatesLocation))
            {
                throw new CliException($"Can't find templates location. Looked at '{templatesLocation}'");
            }

            return FileSystemHelpers.ReadAllTextFromFile(templatesLocation);
        }


        // TODO: Remove this method once a solution for templates has been found
        private static async Task<IEnumerable<Template>> GetStaticTemplates()
        {
            var templatesList = new string[] {
                "BlobTrigger-Python-Preview-Append",
                /*"CosmosDBTrigger-Python-Preview-Append",
                "EventHubTrigger-Python-Preview-Append",*/
                "HttpTrigger-Python-Preview-Append",
                /*"QueueTrigger-Python-Preview-Append",
                "ServiceBusQueueTrigger-Python-Preview-Append",
                "ServiceBusTopicTrigger-Python-Preview-Append",*/
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
            // var gitIgnoreTest = StaticResources.GitIgnore;
            Template template = new Template();
            template.Id = templateName;
            var metaFileName = $"{templateName}.metadata.json";
            var metaContentStr = await StaticResources.GetValue(metaFileName);
            template.Metadata = JsonConvert.DeserializeObject<TemplateMetadata>(
                await StaticResources.GetValue($"{templateName}.metadata.json"
            ));
            template.Files = new Dictionary<string, string> {
                { PythonProgrammingModelMainFileKey, await StaticResources.GetValue($"{templateName}.function_app.py") },
                { PythonProgrammingModelNewFileKey, await StaticResources.GetValue($"{templateName}.New.function_app.py") },
                { PythonProgrammingModelMainNewFileKey, await StaticResources.GetValue($"{templateName}.Main.function_app.py") }
        };
            template.Metadata.ProgrammingModel = true;
            return template;
        }


        public async Task Deploy(string name, string fileName, Template template)
        {
            // todo: this logic will change with the new template schema. 
            if (template.Metadata.ProgrammingModel && template.Metadata.Language.Equals("Python", StringComparison.OrdinalIgnoreCase))
            {
                await DeployNewPythonProgrammingModel(name, fileName, template);
            }
            // todo: Temporary logic. This logic will change with the new template schema. 
            else if (template.Id.EndsWith("JavaScript-4.x") || template.Id.EndsWith("TypeScript-4.x"))
            {
                await DeployNewNodeProgrammingModel(name, fileName, template);
            }
            else
            {
                await DeployLegacyModel(name, template);
            }

            await InstallExtensions(template);
        }

        private async Task DeployNewNodeProgrammingModel (string functionName, string fileName, Template template)
        {
            var templateFiles = template.Files.Where(kv => !kv.Key.EndsWith(".dat"));
            var fileList = new Dictionary<string, string>();

            // Running the validations here. There is no change in the user data in this loop.
            foreach (var file in templateFiles)
            {
                fileName = fileName ?? ReplaceFunctionNamePlaceholder(file.Key, functionName);
                var filePath = Path.Combine(Path.Combine(Environment.CurrentDirectory, "src", "functions"), fileName);
                AskToRemoveFileIfExists(filePath, functionName);
                fileList.Add(filePath, ReplaceFunctionNamePlaceholder(file.Value, functionName));
            }

            foreach (var filePath in fileList.Keys)
            {
                RemoveFileIfExists(filePath);
                ColoredConsole.WriteLine($"Creating a new file {filePath}");
                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, fileList[filePath]);
            }
        }

        private async Task DeployNewPythonProgrammingModel(string functionName, string fileName, Template template)
        {
            var files = template.Files.Where(kv => !kv.Key.EndsWith(".dat"));

            if (files.Count() != 3)
            {
                throw new CliException($"The function with the name {functionName} couldn't be created. We couldn't find the expected files in the template.");
            }

            var mainFilePath = Path.Combine(Environment.CurrentDirectory, PythonProgrammingModelMainFileKey);
            var mainFileContent = await FileSystemHelpers.ReadAllTextFromFileAsync(mainFilePath);

            // Verify that function doesn't exist
            var functionDeclartion = $"@app.function_name(name=\"{functionName}\")";
            if (mainFileContent.Contains(functionDeclartion))
            {
                throw new CliException($"The function with the name {functionName} already exists.");
            }

            // Verify the target file doesn't exist. Delete with permission if it already exists. 
            //fileName = fileName ?? $"{functionName}_function.py";

            if (!string.IsNullOrEmpty(fileName))
            {
                var filePath = Path.Combine(Environment.CurrentDirectory, fileName);
                AskToRemoveFileIfExists(filePath, functionName, removeFile: true);
            }

            // Create/Update the needed files. 
            foreach (var file in files)
            {
                var fileContent = file.Value.Replace("FunctionName", functionName);
                if (file.Key == PythonProgrammingModelMainFileKey && !string.IsNullOrEmpty(fileName))
                {
                    ColoredConsole.WriteLine($"Appending to {mainFilePath}");
                    mainFileContent = $"{mainFileContent}{Environment.NewLine}{Environment.NewLine}{fileContent}";
                    var importLine = $"from {Path.GetFileNameWithoutExtension(fileName)} import {functionName}Impl";
                    // Add the import line for new file.
                    var funcImportLine = "import azure.functions as func";
                    mainFileContent = mainFileContent.Replace(funcImportLine, $"{funcImportLine}{Environment.NewLine}{importLine}");

                    // Update the file. 
                    await FileSystemHelpers.WriteAllTextToFileAsync(mainFilePath, mainFileContent);
                }
                else if (file.Key == PythonProgrammingModelNewFileKey && !string.IsNullOrEmpty(fileName))
                {
                    var filePath = Path.Combine(Environment.CurrentDirectory, fileName);
                    ColoredConsole.WriteLine($"Creating a new file {filePath}");
                    await FileSystemHelpers.WriteAllTextToFileAsync(filePath, fileContent);
                }
                if (file.Key == PythonProgrammingModelMainNewFileKey && string.IsNullOrEmpty(fileName))
                {
                    ColoredConsole.WriteLine($"Appending to {mainFilePath}");
                    mainFileContent = $"{mainFileContent}{Environment.NewLine}{Environment.NewLine}{fileContent}";
                    // Update the file. 
                    await FileSystemHelpers.WriteAllTextToFileAsync(mainFilePath, mainFileContent);
                }
            }
        }

        private static void AskToRemoveFileIfExists(string filePath, string functionName, bool removeFile = false)
        {
            var fileExists = FileSystemHelpers.FileExists(filePath);
            if (fileExists)
            {
                // Once we get the confirmation of overwriting all files then we will overwrite. 
                var response = "n";
                do
                {
                    ColoredConsole.Write($"A file with the name {Path.GetFileName(filePath)} already exists. Overwrite [y/n]? [n] ");
                    response = Console.ReadLine();
                } while (response != "n" && response != "y");
                if (response == "n")
                {
                    throw new CliException($"The function with the name {functionName} couldn't be created.");
                }
            }

            if (removeFile)
            {
                RemoveFileIfExists(filePath);
            }
        }

        private static void RemoveFileIfExists(string filePath)
        {
            if (FileSystemHelpers.FileExists(filePath))
            {
                FileSystemHelpers.FileDelete(filePath);
            }
        }

        private async Task DeployLegacyModel(string name, Template template)
        {
            var path = Path.Combine(Environment.CurrentDirectory, name);
            if (FileSystemHelpers.DirectoryExists(path))
            {
                var response = "n";
                do
                {
                    ColoredConsole.Write($"A directory with the name {name} already exists. Overwrite [y/n]? [n] ");
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
        }

        private async Task InstallExtensions(Template template)
        {
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

        private string ReplaceFunctionNamePlaceholder(string str, string functionName)
        {
            return str?.Replace("%functionName%", functionName) ?? str;
        }
    }
}