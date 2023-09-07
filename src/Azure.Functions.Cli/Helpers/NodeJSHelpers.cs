using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class NodeJSHelpers
    {
        public static async Task SetupProject(ProgrammingModel programmingModel, string language)
        {
            if (programmingModel == ProgrammingModel.V4)
            {
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
