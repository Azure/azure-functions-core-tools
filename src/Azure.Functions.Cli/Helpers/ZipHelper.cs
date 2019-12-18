using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Ionic.Zip;
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
                ColoredConsole.WriteLine(Yellow("Performing remote build for functions project."));
            } else if (buildOption == BuildOption.Local)
            {
                ColoredConsole.WriteLine(Yellow("Performing local build for functions project."));
            }

            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.python && !noBuild)
            {
                return await PythonHelpers.GetPythonDeploymentPackage(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot, buildNativeDeps, buildOption, additionalPackages);
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet && !ignoreDotNetCheck && !noBuild && buildOption != BuildOption.Remote)
            {
                throw new CliException("Pack command doesn't work for dotnet functions");
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet && buildOption == BuildOption.Remote)
            {
                // Remote build for dotnet does not require bin and obj folders. They will be generated during the oryx build
                return await CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false, new string[] { "bin", "obj" }), functionAppRoot);
            } else
            {
                return await CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false), functionAppRoot);
            }
        }

        public static async Task<Stream> CreateZip(IEnumerable<string> files, string rootPath)
        {
            var zipFilePath = Path.GetTempFileName();

            if (GoZipExists(out string goZipLocation))
            {
                return await CreateGoZip(files, rootPath, zipFilePath, goZipLocation);
            }

            ColoredConsole.WriteLine(Yellow("Could not find gozip for packaging. Using DotNetZip to package. " +
                "This may cause problems preserving file permissions when using in a Linux based environment."));

            return CreateDotNetZip(files, rootPath, zipFilePath);
        }

        public static bool GoZipExists(out string fileLocation)
        {
            // It can be gozip.exe or gozip
            fileLocation = Directory.GetFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .Where(f => Path.GetFileNameWithoutExtension(f).Equals(Constants.GoZipFileName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (fileLocation != null)
            {
                return true;
            }

            return false;
        }

        public static Stream CreateDotNetZip(IEnumerable<string> files, string rootPath, string zipFilePath)
        {
            const int defaultBufferSize = 4096;
            var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, defaultBufferSize, FileOptions.DeleteOnClose);
            using (ZipFile zip = new ZipFile())
            {
                zip.CompressionLevel = Ionic.Zlib.CompressionLevel.BestSpeed;
                foreach (var file in files)
                {
                    zip.AddFile(file.FixFileNameForZip(rootPath));
                }
                zip.Save(fileStream);
            }
            fileStream.Seek(0, SeekOrigin.Begin);
            return fileStream;
        }

        public static async Task<Stream> CreateGoZip(IEnumerable<string> files, string rootPath, string zipFilePath, string goZipLocation)
        {
            var contentsFile = Path.GetTempFileName();
            await File.WriteAllLinesAsync(contentsFile, files);

            var goZipExe = new Executable(goZipLocation, $"-base-dir {rootPath} -input-file {contentsFile} -output {zipFilePath}");
            await goZipExe.RunAsync();
            return new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        }

        public static string FixFileNameForZip(this string value, string zipRoot)
        {
            return value.Substring(zipRoot.Length).TrimStart(new[] { '\\', '/' }).Replace('\\', '/');
        }
    }
}