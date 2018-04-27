using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Fclp;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "pack", HelpText = "Pack function app into a zip that's ready to run.", ShowInHelp = false)]
    internal class PackAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string FolderName { get; set; } = string.Empty;
        public string OutputPath { get; set; }

        public PackAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('o', "output")
                .WithDescription("output path for the packed archive")
                .Callback(o => OutputPath = o);

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderName = args.First();
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var functionAppRoot = string.IsNullOrEmpty(FolderName)
                ? Path.Combine(Environment.CurrentDirectory, FolderName)
                : ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            string outputPath;
            if (string.IsNullOrEmpty(OutputPath))
            {
                outputPath = Path.Combine(Environment.CurrentDirectory, $"{Path.GetFileName(functionAppRoot)}.zip");
            }
            else
            {
                outputPath = Path.Combine(Environment.CurrentDirectory, OutputPath);
                if (FileSystemHelpers.DirectoryExists(outputPath))
                {
                    outputPath = Path.Combine(outputPath, $"{Path.GetFileName(functionAppRoot)}.zip");
                }
            }

            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)))
            {
                throw new CliException($"Can't find {Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)}");
            }

            var workerRuntime = _secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase)).Value;
            var workerRuntimeEnum = string.IsNullOrEmpty(workerRuntime) ? WorkerRuntime.None : WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntime);

            var zipStream = await GetAppZipFile(workerRuntimeEnum, functionAppRoot);
            FileSystemHelpers.WriteToFile(outputPath, zipStream);
        }

        public static async Task<Stream> GetAppZipFile(WorkerRuntime workerRuntime, string functionAppRoot, GitIgnoreParser ignoreParser = null)
        {
            var gitIgnorePath = Path.Combine(functionAppRoot, Constants.FuncIgnoreFile);
            if (ignoreParser == null && FileSystemHelpers.FileExists(gitIgnorePath))
            {
                ignoreParser = new GitIgnoreParser(await FileSystemHelpers.ReadAllTextFromFileAsync(gitIgnorePath));
            }

            if (workerRuntime == WorkerRuntime.python)
            {
                return await PythonHelpers.GetPythonDeploymentPackage(GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot);
            }
            else
            {
                return CreateZip(GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot);
            }
        }

        public static IEnumerable<string> GetLocalFiles(string path, GitIgnoreParser ignoreParser = null, bool returnIgnored = false)
        {
            var ignoredDirectories = new[] { ".git", ".vscode" };
            var ignoredFiles = new[] { ".funcignore", ".gitignore", "appsettings.json", "local.settings.json", "project.lock.json" };

            foreach (var file in FileSystemHelpers.GetFiles(path, ignoredDirectories, ignoredFiles))
            {
                if (preCondition(file))
                {
                    yield return file;
                }
            }

            bool preCondition(string file)
            {
                var fileName = file.Replace(path, string.Empty).Trim(Path.DirectorySeparatorChar).Replace("\\", "/");
                return (returnIgnored ? ignoreParser?.Denies(fileName) : ignoreParser?.Accepts(fileName)) ?? true;
            }
        }

        public static Stream CreateZip(IEnumerable<string> files, string rootPath)
        {
            var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var fileName in files)
                {
                    zip.AddFile(fileName, fileName, rootPath);
                }
            }
            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }
    }
}