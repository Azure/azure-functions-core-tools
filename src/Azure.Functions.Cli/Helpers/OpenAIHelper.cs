using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.Helpers
{
    public class OpenAIHelper
    {

        public async static Task CreateOpenAIPluginJsonFile()
        {
            var wellKnownPath = Path.Join(Environment.CurrentDirectory, Constants.WellKnown);
            var aiPluginPath = Path.Join(wellKnownPath, Constants.AIPlugin);

            if (!FileSystemHelpers.DirectoryExists(wellKnownPath) && !FileSystemHelpers.FileExists(aiPluginPath))
            {
                FileSystemHelpers.EnsureDirectory(wellKnownPath);
                ColoredConsole.WriteLine($"Writing {Constants.AIPlugin} at {aiPluginPath}");
                string aiPluginJsonFileContent = await StaticResources.AIPluginJsonFile;
                await FileSystemHelpers.WriteAllTextToFileAsync(aiPluginPath, aiPluginJsonFileContent);
            }
            else
            {
                ColoredConsole.WriteLine($"{Constants.AIPlugin} already exists at {aiPluginPath}. Skipped!");
            }
        }

        public async static Task CreateOpenAIOpenAPIYamlFile()
        {
            var openAIOpenAPIYaml = Path.Join(Environment.CurrentDirectory, Constants.OpenAPIYaml);

            if (!FileSystemHelpers.FileExists(openAIOpenAPIYaml))
            {
                ColoredConsole.WriteLine($"Writing {Constants.OpenAPIYaml} at {openAIOpenAPIYaml}");
                string openAIOpenAPIYamlFileContent = await StaticResources.OpenAIOpenAPIYamlFile;
                await FileSystemHelpers.WriteAllTextToFileAsync(openAIOpenAPIYaml, openAIOpenAPIYamlFileContent);
            }
            else
            {
                ColoredConsole.WriteLine($"{Constants.OpenAPIYaml} already exists at {openAIOpenAPIYaml}. Skipped!");
            }
        }
    }
}
