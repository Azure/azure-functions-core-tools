
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;

namespace Azure.Functions.Cli.Helpers
{
    public static class ZipHelper
    {
        public static async Task<Stream> GetAppZipFile(WorkerRuntime workerRuntime, string functionAppRoot, bool buildNativeDeps, bool noBundler, GitIgnoreParser ignoreParser = null, string additionalPackages = null, bool ignoreDotNetCheck = false)
        {
            var gitIgnorePath = Path.Combine(functionAppRoot, Constants.FuncIgnoreFile);
            if (ignoreParser == null && FileSystemHelpers.FileExists(gitIgnorePath))
            {
                ignoreParser = new GitIgnoreParser(await FileSystemHelpers.ReadAllTextFromFileAsync(gitIgnorePath));
            }

            if (workerRuntime == WorkerRuntime.python)
            {
                return await PythonHelpers.GetPythonDeploymentPackage(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot, buildNativeDeps, noBundler, additionalPackages);
            }
            else if (workerRuntime == WorkerRuntime.dotnet && !ignoreDotNetCheck)
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