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

            return JsonConvert.DeserializeObject<IEnumerable<Template>>(templatesJson);
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

        

        public async Task Deploy(string name, Template template)
        {
            if (template.Metadata.ProgrammingModel)
            {
                await DeployNewProgrammingModel(name, template);
            }
            else
            {
                await DeployLegacyModel(name, template);
            }

            await InstallExtensions(template);
        }

        private async Task DeployNewProgrammingModel(string name, Template template)
        {
            var path = Path.Combine(Environment.CurrentDirectory);
            var files = template.Files.Where(kv => !kv.Key.EndsWith(".dat"));
            var existingFileNames = files.Where(file => FileSystemHelpers.FileExists(file.Key)).Select(file => file.Key);

            if (existingFileNames.Any())
            {
                // Once we get the confirmation of overwriting all files then we will overwrite. 
                foreach(var existingFileName  in existingFileNames) 
                {
                    var response = "n";
                    do
                    {
                        ColoredConsole.Write($"A file with the name {existingFileName} already exists. Overwrite [y/n]? [n] ");
                        response = Console.ReadLine();
                    } while (response != "n" && response != "y");
                    if (response == "n")
                    {
                        return;
                    }
                }
            }

            foreach (var existingFileName in existingFileNames)
            {
                if (FileSystemHelpers.FileExists(path))
                {
                    FileSystemHelpers.FileDelete(path);
                }
            }

            foreach (var file in files)
            {
                var filePath = Path.Combine(path, file.Key);
                ColoredConsole.WriteLine($"Writing {filePath}");
                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, file.Value);
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
    }
}