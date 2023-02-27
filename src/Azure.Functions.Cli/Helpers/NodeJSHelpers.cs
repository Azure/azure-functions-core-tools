using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class NodeJSHelpers
    {
        public static void PrintV4ReferenceMessage()
        {
            ColoredConsole.Write(AdditionalInfoColor("The new Node.js programming model is in public preview. Learn more at "));
            PrintNodeV4WikiLink();
        }

        public static void PrintV4AwarenessMessage()
        {
            ColoredConsole.Write(AdditionalInfoColor("Did you know? There is a new Node.js programming model in public preview. Learn how you can try it out today at "));
            PrintNodeV4WikiLink();
        }

        public static void PrintNodeV4WikiLink()
        {
            ColoredConsole.WriteLine(LinksColor("https://aka.ms/AzFuncNodeV4"));
        }

        public static async Task SetupProject(ProgrammingModel programmingModel, string language)
        {
            if (programmingModel == ProgrammingModel.V4)
            {
                PrintV4ReferenceMessage();
                if (language == Constants.Languages.TypeScript)
                {
                    await FileSystemHelpers.WriteFileIfNotExists(Constants.PackageJsonFileName, await StaticResources.PackageJsonTsV4);
                }
                else
                {
                    await FileSystemHelpers.WriteFileIfNotExists(Constants.PackageJsonFileName, await StaticResources.PackageJsonJsV4);
                }

                FileSystemHelpers.EnsureDirectory("src/functions");
            }
            else
            {
                PrintV4AwarenessMessage();
                if (language == Constants.Languages.TypeScript)
                {
                    await FileSystemHelpers.WriteFileIfNotExists(Constants.PackageJsonFileName, await StaticResources.PackageJsonTs);
                }
                else
                {
                    await FileSystemHelpers.WriteFileIfNotExists(Constants.PackageJsonFileName, await StaticResources.PackageJsonJs);
                }
            }

            await FileSystemHelpers.WriteFileIfNotExists(".funcignore", await StaticResources.FuncIgnore);
            if (language == Constants.Languages.TypeScript) {
                await FileSystemHelpers.WriteFileIfNotExists("tsconfig.json", await StaticResources.TsConfig);
            }
        }
    }
}
