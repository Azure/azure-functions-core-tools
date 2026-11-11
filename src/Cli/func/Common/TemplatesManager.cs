// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    internal class TemplatesManager : ITemplatesManager
    {
        internal const string ConnectorTriggerType = "connectorTrigger";
        internal const string ConnectorPollingEndpointSetting = "ConnectorNamespace__pollingEndpoint";
        internal const string ConnectorPollingEndpointPlaceholder = "https://<connector-namespace-host>";
        internal const string ConnectorTriggerConfigPlaceholder = "<poll-trigger-config-name>";
        private const string BundleTemplatesLockFileName = "func_bundle_templates.lock";

        private static readonly Dictionary<string, string> _connectorDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["OutlookTrigger"] = "Office 365 Outlook",
            ["SharepointTrigger"] = "SharePoint",
            ["OneDriveTrigger"] = "OneDrive for Business",
            ["DataverseTrigger"] = "Dataverse",
            ["TeamsTrigger"] = "Microsoft Teams",
        };

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

                    // Get content provider if not offline; otherwise just get templates within CLI
                    if (!GlobalCoreToolsSettings.IsOfflineMode)
                    {
                        var contentProvider = ExtensionBundleHelper.GetExtensionBundleContentProvider();
                        return await contentProvider.GetTemplates();
                    }

                    return GetTemplatesJson();
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
            if (template.Id.EndsWith("JavaScript-4.x", StringComparison.OrdinalIgnoreCase) || template.Id.EndsWith("TypeScript-4.x", StringComparison.OrdinalIgnoreCase))
            {
                await DeployNewNodeProgrammingModel(name, fileName, template);
                await EnsureDurableFunctionsNpmPackage(template);
                await EnsureConnectorTriggerScaffolding(name, fileName, template);
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
                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Creating a new file {filePath}"));
                }

                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, fileList[filePath]);
            }
        }

        private async Task EnsureDurableFunctionsNpmPackage(Template template)
        {
            var triggerType = template.Metadata?.TriggerType;
            var isDurableTemplate =
                string.Equals(triggerType, Constants.OrchestrationTriggerType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(triggerType, Constants.EntityTriggerType, StringComparison.OrdinalIgnoreCase);

            if (!isDurableTemplate)
            {
                return;
            }

            var packageJsonPath = Path.Combine(Environment.CurrentDirectory, Constants.PackageJsonFileName);
            if (!FileSystemHelpers.FileExists(packageJsonPath))
            {
                return;
            }

            var dependencyAdded = false;
            try
            {
                var packageJson = JObject.Parse(await FileSystemHelpers.ReadAllTextFromFileAsync(packageJsonPath));
                if (packageJson["dependencies"] is not JObject dependencies)
                {
                    dependencies = new JObject();
                    packageJson["dependencies"] = dependencies;
                }

                if (dependencies[Constants.DurableFunctionsNpmPackageName] == null)
                {
                    dependencies[Constants.DurableFunctionsNpmPackageName] = Constants.DurableFunctionsNpmPackageVersion;
                    await FileSystemHelpers.WriteAllTextToFileAsync(packageJsonPath, JsonConvert.SerializeObject(packageJson, Formatting.Indented));
                    dependencyAdded = true;
                    ColoredConsole.WriteLine(AdditionalInfoColor($"Added '{Constants.DurableFunctionsNpmPackageName}' to {Constants.PackageJsonFileName}."));
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(WarningColor($"Warning: Unable to add '{Constants.DurableFunctionsNpmPackageName}' to {Constants.PackageJsonFileName}. You must add it manually. {ex.Message}"));
                return;
            }

            if (!dependencyAdded)
            {
                return;
            }

            if (GlobalCoreToolsSettings.IsOfflineMode)
            {
                ColoredConsole.WriteLine(WarningColor("Skipping \"npm install\" because the CLI is running in offline mode. You must run \"npm install\" manually when network is available."));
                return;
            }

            if (!CommandChecker.CommandExists("npm"))
            {
                ColoredConsole.WriteLine(WarningColor("Skipping \"npm install\" because npm was not found. You must run \"npm install\" manually."));
                return;
            }

            try
            {
                var exitCode = await NpmHelper.Install();
                if (exitCode != 0)
                {
                    ColoredConsole.Error.WriteLine(WarningColor("Warning: 'npm install' did not complete successfully. You must run \"npm install\" manually."));
                }
            }
            catch (Exception)
            {
                ColoredConsole.Error.WriteLine(WarningColor("Warning: You must run \"npm install\" manually"));
            }
        }

        internal static bool IsConnectorTriggerTemplate(Template template) =>
            string.Equals(template?.Metadata?.TriggerType, ConnectorTriggerType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the app settings (with placeholder values) required by a connector trigger template.
        /// </summary>
        internal static IDictionary<string, string> GetConnectorTriggerSettings(Template template)
        {
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var content in template.Files?.Values ?? Enumerable.Empty<string>())
            {
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(content, "%([A-Za-z0-9_]+)%"))
                {
                    var key = match.Groups[1].Value;
                    if (!key.Equals("functionName", StringComparison.OrdinalIgnoreCase) && !settings.ContainsKey(key))
                    {
                        settings[key] = ConnectorTriggerConfigPlaceholder;
                    }
                }
            }

            settings[ConnectorPollingEndpointSetting] = ConnectorPollingEndpointPlaceholder;
            return settings;
        }

        internal static string BuildConnectorTriggerReadme(string readmeTemplate, string functionName, string fileName, Template template, IDictionary<string, string> settings)
        {
            var isTypeScript = template.Id.EndsWith("TypeScript-4.x", StringComparison.OrdinalIgnoreCase);
            var prefix = template.Id.Split('-')[0];
            var displayName = _connectorDisplayNames.TryGetValue(prefix, out var dn) ? dn : prefix;

            var rows = settings.Select(kv => kv.Key == ConnectorPollingEndpointSetting
                ? $"| `{kv.Key}` | Endpoint of the connector namespace (used by `connection: 'ConnectorNamespace'`). |"
                : $"| `{kv.Key}` | Name of the {displayName} poll trigger config in the connector namespace. |");

            var typeScriptNote = isTypeScript
                ? Environment.NewLine + "For TypeScript, make sure `tsconfig.json` sets `\"module\": \"Node16\"` and `\"moduleResolution\": \"Node16\"`, then run `npm run build`." + Environment.NewLine
                : string.Empty;

            return readmeTemplate
                .Replace("{FunctionName}", functionName)
                .Replace("{ConnectorDisplayName}", displayName)
                .Replace("{Language}", isTypeScript ? "TypeScript" : "JavaScript")
                .Replace("{TemplateName}", template.Metadata?.Name ?? template.Id)
                .Replace("{FileName}", fileName)
                .Replace("{SettingsTable}", string.Join(Environment.NewLine, rows))
                .Replace("{TypeScriptNote}", typeScriptNote);
        }

        private async Task EnsureConnectorTriggerScaffolding(string functionName, string fileName, Template template)
        {
            if (!IsConnectorTriggerTemplate(template))
            {
                return;
            }

            var settings = GetConnectorTriggerSettings(template);

            try
            {
                var localSettingsPath = Path.Combine(Environment.CurrentDirectory, Constants.LocalSettingsJsonFileName);
                var localSettings = FileSystemHelpers.FileExists(localSettingsPath)
                    ? JObject.Parse(await FileSystemHelpers.ReadAllTextFromFileAsync(localSettingsPath))
                    : new JObject { ["IsEncrypted"] = false };

                if (localSettings["Values"] is not JObject values)
                {
                    values = new JObject();
                    localSettings["Values"] = values;
                }

                var added = new List<string>();
                var settingsWriter = new AppSettingsFile(localSettingsPath)
                {
                    IsEncrypted = localSettings.Value<bool?>("IsEncrypted") ?? true
                };
                foreach (var setting in settings)
                {
                    if (!values.TryGetValue(setting.Key, StringComparison.OrdinalIgnoreCase, out _))
                    {
                        settingsWriter.SetSecret(setting.Key, setting.Value);
                        values[setting.Key] = settingsWriter.Values[setting.Key];
                        added.Add(setting.Key);
                    }
                }

                if (added.Count > 0)
                {
                    await FileSystemHelpers.WriteAllTextToFileAsync(localSettingsPath, JsonConvert.SerializeObject(localSettings, Formatting.Indented));
                    ColoredConsole.WriteLine(AdditionalInfoColor($"Added connector trigger settings to {Constants.LocalSettingsJsonFileName}: {string.Join(", ", added)}. Replace the placeholder values before running."));
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(WarningColor($"Warning: Unable to update {Constants.LocalSettingsJsonFileName}. Add these settings manually: {string.Join(", ", settings.Keys)}. {ex.Message}"));
            }

            try
            {
                var readmePath = Path.Combine(Environment.CurrentDirectory, $"{functionName}.README.md");
                if (!FileSystemHelpers.FileExists(readmePath))
                {
                    var functionFileName = fileName ?? ReplaceFunctionNamePlaceholder(template.Files.Keys.First(k => !k.EndsWith(".dat")), functionName);
                    var readme = BuildConnectorTriggerReadme(await StaticResources.ConnectorTriggerReadme, functionName, functionFileName, template, settings);
                    await FileSystemHelpers.WriteAllTextToFileAsync(readmePath, readme);
                    ColoredConsole.WriteLine(AdditionalInfoColor($"Created {Path.GetFileName(readmePath)} with setup instructions. Visit aka.ms/connectortrigger for more details."));
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(WarningColor($"Warning: Unable to create the connector trigger README. Visit aka.ms/connectortrigger for instructions. {ex.Message}"));
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
                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Writing {filePath}"));
                }

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
                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Creating a new file {filePath}"));
                }

                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, sourceContent);
            }
            else
            {
                if (!isAppend)
                {
                    AskToRemoveFileIfExists(filePath);
                }

                var fileContent = await FileSystemHelpers.ReadAllTextFromFileAsync(filePath);
                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Appending to {filePath}"));
                }

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
