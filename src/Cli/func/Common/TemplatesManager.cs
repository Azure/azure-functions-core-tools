﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Common
{
    internal class TemplatesManager : ITemplatesManager
    {
        private const string BundleTemplatesLockFileName = "func_bundle_templates.lock";
        private readonly ISecretsManager _secretsManager;

        public TemplatesManager(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        /// <summary>
        /// Gets new (v2) templates.
        /// </summary>
        public Task<IEnumerable<NewTemplate>> NewTemplates
        {
            get
            {
                return GetV2Templates();
            }
        }

        public Task<IEnumerable<Template>> Templates
        {
            get
            {
                return GetTemplates();
            }
        }

        public Task<IEnumerable<UserPrompt>> UserPrompts
        {
            get
            {
                return GetV2UserPrompts();
            }
        }

        public async Task<string> GetExtensionBundleFileContent(string path)
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
            var bundlePath = await extensionBundleManager.GetExtensionBundlePath();
            string contentFilePath = Path.Combine(bundlePath, path);

            if (FileSystemHelpers.FileExists(contentFilePath))
            {
                return await FileSystemHelpers.ReadAllTextFromFileAsync(contentFilePath);
            }
            else
            {
                return null;
            }
        }

        private static async Task<IEnumerable<Template>> GetTemplates()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
            string templatesJson;

            if (extensionBundleManager.IsExtensionBundleConfigured())
            {
                templatesJson = await FileLockHelper.WithFileLockAsync(BundleTemplatesLockFileName, async () =>
                {
                    await ExtensionBundleHelper.GetExtensionBundle();
                    var contentProvider = ExtensionBundleHelper.GetExtensionBundleContentProvider();
                    return await contentProvider.GetTemplates();
                });
            }
            else
            {
                templatesJson = GetTemplatesJson();
            }

            if (string.IsNullOrEmpty(templatesJson))
            {
                throw new CliException("Templates JSON is empty. Please check the templates location.");
            }

            var templates = JsonConvert.DeserializeObject<IEnumerable<Template>>(templatesJson);
            templates = templates.Concat(await GetNodeV4TemplatesJson()).ToList();
            return templates;
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

        private static async Task<string> GetV2TemplatesJson()
        {
            var templatesLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "templates-v2", "templates.json");
            if (!FileSystemHelpers.FileExists(templatesLocation))
            {
                throw new CliException($"Can't find v2 templates location. Looked at '{templatesLocation}'");
            }

            return await FileSystemHelpers.ReadAllTextFromFileAsync(templatesLocation);
        }

        private static async Task<string> GetV2UserPromptsJson()
        {
            var userPrompotsLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "templates-v2", "userPrompts.json");
            if (!FileSystemHelpers.FileExists(userPrompotsLocation))
            {
                throw new CliException($"Can't find v2 user prompts location. Looked at '{userPrompotsLocation}'");
            }

            return await FileSystemHelpers.ReadAllTextFromFileAsync(userPrompotsLocation);
        }

        private static async Task<IEnumerable<Template>> GetNodeV4TemplatesJson()
        {
            var staticTemplateJson = await StaticResources.GetValue($"node-v4-templates.json");
            return JsonConvert.DeserializeObject<IEnumerable<Template>>(staticTemplateJson);
        }

        public async Task Deploy(string name, string fileName, Template template)
        {
            if (template.Id.EndsWith("JavaScript-4.x") || template.Id.EndsWith("TypeScript-4.x"))
            {
                await DeployNewNodeProgrammingModel(name, fileName, template);
            }
            else
            {
                await DeployTraditionalModel(name, template);
            }

            await InstallExtensions(template);
        }

        public async Task Deploy(TemplateJob job, NewTemplate template, IDictionary<string, string> variables)
        {
            foreach (var actionName in job.Actions)
            {
                var action = template.Actions.First(x => x.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));
                if (action.ActionType.Equals(Constants.ShowMarkdownPreviewActionType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await RunTemplateActionAction(template, action, variables);
            }
        }

        private async Task DeployNewNodeProgrammingModel(string functionName, string fileName, Template template)
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

        private static void AskToRemoveFileIfExists(string filePath, string functionName = null, bool removeFile = false)
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
                }
                while (response != "n" && response != "y");
                if (response == "n")
                {
                    var message = "The function couldn't be created.";
                    if (!string.IsNullOrEmpty(functionName))
                    {
                        message = $"The function with the name {functionName} couldn't be created.";
                    }

                    throw new CliException(message);
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

        private async Task DeployTraditionalModel(string name, Template template)
        {
            var path = Path.Combine(Environment.CurrentDirectory, name);
            if (FileSystemHelpers.DirectoryExists(path))
            {
                var response = "n";
                do
                {
                    ColoredConsole.Write($"A directory with the name {name} already exists. Overwrite [y/n]? [n] ");
                    response = Console.ReadLine();
                }
                while (response != "n" && response != "y");
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

        public async Task<IEnumerable<NewTemplate>> GetV2Templates()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
            string templateJson;
            if (extensionBundleManager.IsExtensionBundleConfigured())
            {
                templateJson = await GetExtensionBundleFileContent(Path.Combine("StaticContent", "v2", "templates", Constants.TemplatesExtensionBundleFileName));
            }
            else
            {
                templateJson = await GetV2TemplatesJson();
            }

            if (string.IsNullOrEmpty(templateJson))
            {
                throw new CliException("Templates JSON is empty. Please check the templates location.");
            }

            return JsonConvert.DeserializeObject<IEnumerable<NewTemplate>>(templateJson);
        }

        public async Task<IEnumerable<UserPrompt>> GetV2UserPrompts()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();

            string userPromptJson;
            if (extensionBundleManager.IsExtensionBundleConfigured())
            {
                userPromptJson = await GetExtensionBundleFileContent(Path.Combine("StaticContent", "v2", "bindings", Constants.UserPromptExtensionBundleFileName));
            }
            else
            {
                userPromptJson = await GetV2UserPromptsJson();
            }

            return JsonConvert.DeserializeObject<UserPrompt[]>(userPromptJson);
        }

        private async Task RunTemplateActionAction(NewTemplate template, TemplateAction action, IDictionary<string, string> variables)
        {
            if (action.ActionType == "GetTemplateFileContent")
            {
                GetTemplateFileContent(template, action, variables);
                return;
            }
            else if (action.ActionType == "AppendToFile")
            {
                await WriteFunctionBody(template, action, variables, isAppend: true);
                return;
            }
            else if (action.ActionType == "WriteToFile")
            {
                await WriteFunctionBody(template, action, variables);
                return;
            }

            throw new CliException($"Template Failure. Action type '{action.ActionType}' is not supported.");
        }

        private void GetTemplateFileContent(NewTemplate template, TemplateAction action, IDictionary<string, string> variables)
        {
            if (!template.Files.ContainsKey(action.FilePath))
            {
                throw new CliException($"Template Failure. File name '{action.FilePath}' is not found in the template.");
            }

            var fileContent = template.Files[action.FilePath];
            variables.Add(action.AssignTo, fileContent);
        }

        private async Task WriteFunctionBody(NewTemplate template, TemplateAction action, IDictionary<string, string> variables, bool isAppend = false)
        {
            if (!variables.ContainsKey(action.Source))
            {
                throw new CliException($"Template Failure. Source '{action.Source}' value is not found.");
            }

            var fileName = variables[action.FilePath];
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Constants.PySteinFunctionAppPy;
            }

            var sourceContent = variables[action.Source];
            if (action.ReplaceTokens)
            {
                sourceContent = ReplaceTokensInSource(action, variables);
            }

            var filePath = Path.Combine(Environment.CurrentDirectory, fileName);
            if (!FileSystemHelpers.FileExists(filePath))
            {
                ColoredConsole.WriteLine($"Creating a new file {filePath}");
                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, sourceContent);
            }
            else
            {
                if (!isAppend)
                {
                    AskToRemoveFileIfExists(filePath);
                }

                var fileContent = await FileSystemHelpers.ReadAllTextFromFileAsync(filePath);
                ColoredConsole.WriteLine($"Appending to {filePath}");
                fileContent = $"{fileContent}{Environment.NewLine}{Environment.NewLine}{sourceContent}";

                // Update the file.
                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, fileContent);
            }
        }

        private string ReplaceTokensInSource(TemplateAction action, IDictionary<string, string> variables)
        {
            if (!variables.ContainsKey(action.Source))
            {
                throw new CliException($"Template Failure. Source '{action.Source}' value is not found.");
            }

            var sourceContent = variables[action.Source];

            foreach (var variable in variables)
            {
                if (variable.Key == action.Source)
                {
                    continue;
                }

                var value = variable.Value;

                // this is an special condition. Only filename without extension is replaced.
                if (variable.Key == "$(BLUEPRINT_FILENAME)")
                {
                    value = value[..^Path.GetExtension(value).Length];
                }

                sourceContent = sourceContent.Replace(variable.Key, value);
            }

            return sourceContent;
        }
    }
}
