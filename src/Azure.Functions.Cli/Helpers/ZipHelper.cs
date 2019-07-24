using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Helpers
{
    public static class ZipHelper
    {
        public static async Task<Stream> GetAppZipFile(string functionAppRoot, bool buildNativeDeps, BuildOption buildOption, bool noBuild, GitIgnoreParser ignoreParser = null, string additionalPackages = null, bool ignoreDotNetCheck = false)
        {
            var gitIgnorePath = Path.Combine(functionAppRoot, Constants.FuncIgnoreFile);
            if (ignoreParser == null && FileSystemHelpers.FileExists(gitIgnorePath))
            {
                ignoreParser = new GitIgnoreParser(await FileSystemHelpers.ReadAllTextFromFileAsync(gitIgnorePath));
            }

            if (noBuild)
            {
                ColoredConsole.WriteLine(Yellow("Skipping build event for functions project (--no-build)."));
            } else if (buildOption == BuildOption.Remote)
            {
                ColoredConsole.WriteLine(Yellow("Perform remote build for functions project (--build remote)."));
            }
            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.python && !noBuild)
            {
                return await PythonHelpers.GetPythonDeploymentPackage(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot, buildNativeDeps, buildOption, additionalPackages);
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet && !ignoreDotNetCheck && !noBuild && buildOption != BuildOption.Remote)
            {
                throw new CliException("Pack command doesn't work for dotnet functions");
            }
            else
            {
                return CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot);
            }
        }

        public static Stream CreateZip(IEnumerable<string> files, string rootPath)
        {
            const int defaultBufferSize = 4096;
            var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, defaultBufferSize, FileOptions.DeleteOnClose);

            using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var fileName in files)
                {
                    zip.AddFile(fileName, fileName, rootPath);
                }
            }

            fileStream.Seek(0, SeekOrigin.Begin);
            return fileStream;
        }
    }
}