// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.ArtifactAssembler
{
    internal sealed class ArtifactAssembler
    {
        private const string StagingDirName = "staging";
        private const string InProc8DirectoryName = "in-proc8";
        private const string InProc6DirectoryName = "in-proc6";
        private const string CoreToolsHostDirectoryName = "host";
        private const string OutputArtifactDirectoryName = "coretools-visualstudio";

        /// <summary>
        /// The artifacts for which we want to pack a custom host with it.
        /// This dictionary contains the artifact name and the corresponding runtime identifier value.
        /// </summary>
        private readonly Dictionary<string, string> _customHostArtifacts = new()
        {
            { "Azure.Functions.Cli.min.win-x64", "win-x64" },
            { "Azure.Functions.Cli.min.win-arm64", "win-arm64" }
        };

        private readonly string _inProcArtifactDirectoryName;
        private readonly string _coreToolsHostArtifactDirectoryName;
        private readonly string _inProc6ArtifactName;
        private readonly string _inProc8ArtifactName;
        private readonly string _coreToolsHostArtifactName;
        private readonly string _rootWorkingDirectory;
        private readonly string _stagingDirectory;

        private string _inProc6ExtractedRootDir = string.Empty;
        private string _inProc8ExtractedRootDir = string.Empty;
        private string _coreToolsHostExtractedRootDir = string.Empty;

        internal ArtifactAssembler(string rootWorkingDirectory)
        {
            _inProcArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProcArtifactAlias);
            _coreToolsHostArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostArtifactAlias);
            _inProc6ArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProc6ArtifactName);
            _inProc8ArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProc8ArtifactName);
            _coreToolsHostArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostArtifactZipName);

            _rootWorkingDirectory = rootWorkingDirectory;
            _stagingDirectory = CreateStagingDirectory(_rootWorkingDirectory);
        }

        internal async Task AssembleArtifactsAsync()
        {
            await ExtractDownloadedArtifactsAsync();
            await CreateVisualStudioCoreToolsAsync();
        }

        private static string GetRequiredEnvironmentVariable(string variableName)
        {
            return Environment.GetEnvironmentVariable(variableName)
                ?? throw new InvalidDataException($"The `{variableName}` environment variable value is missing!");
        }

        private async Task ExtractDownloadedArtifactsAsync()
        {
            var inProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _inProcArtifactDirectoryName);
            var coreToolsHostArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _coreToolsHostArtifactDirectoryName);

            var inProc6ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc6ArtifactName);
            var inProc8ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc8ArtifactName);
            var coreToolsHostArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostArtifactName);

            EnsureArtifactDirectoryExist(inProc6ArtifactDirPath);
            EnsureArtifactDirectoryExist(inProc8ArtifactDirPath);
            EnsureArtifactDirectoryExist(coreToolsHostArtifactDirPath);

            var inProc6Task = MoveArtifactsToStagingDirectoryAndExtractIfNeeded(inProc6ArtifactDirPath, Path.Combine(_stagingDirectory, InProc6DirectoryName));
            var inProc8Task = MoveArtifactsToStagingDirectoryAndExtractIfNeeded(inProc8ArtifactDirPath, Path.Combine(_stagingDirectory, InProc8DirectoryName));
            var coreToolsHostTask = MoveArtifactsToStagingDirectoryAndExtractIfNeeded(coreToolsHostArtifactDirPath, Path.Combine(_stagingDirectory, CoreToolsHostDirectoryName));

            await Task.WhenAll(inProc6Task, inProc8Task, coreToolsHostTask)
                .ContinueWith(t =>
                {
                    _inProc6ExtractedRootDir = inProc6Task.Result;
                    _inProc8ExtractedRootDir = inProc8Task.Result;
                    _coreToolsHostExtractedRootDir = coreToolsHostTask.Result;
                });
        }

        private static void EnsureArtifactDirectoryExist(string directoryExist)
        {
            if (!Directory.Exists(directoryExist))
            {
                throw new InvalidOperationException($"Artifact directory '{directoryExist}' not found!");
            }
        }

        private static string CreateStagingDirectory(string rootWorkingDirectory)
        {
            var stagingDirectory = Path.Combine(rootWorkingDirectory, StagingDirName);

            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }

            Directory.CreateDirectory(stagingDirectory);
            Console.WriteLine($"Created staging directory: {stagingDirectory}");

            return stagingDirectory;
        }

        private static async Task<string> MoveArtifactsToStagingDirectoryAndExtractIfNeeded(string artifactZipPath, string destinationDirectory)
        {
            await Task.Run(() => FileUtilities.CopyDirectory(artifactZipPath, destinationDirectory));
            await ExtractZipFilesInDirectoryAsync(artifactZipPath, destinationDirectory);
            return destinationDirectory;
        }

        private async Task CreateVisualStudioCoreToolsAsync()
        {
            // Create a directory to store the assembled artifacts.
            var customHostTargetArtifactDir = Path.Combine(_stagingDirectory, OutputArtifactDirectoryName);
            Directory.CreateDirectory(customHostTargetArtifactDir);

            var packTasks = _customHostArtifacts.Keys.Select(async artifactName =>
            {
                var inProc8ArtifactDirPath = Directory.EnumerateDirectories(_inProc8ExtractedRootDir)
                                          .FirstOrDefault(dir => dir.Contains(artifactName));
                if (inProc8ArtifactDirPath == null)
                {
                    throw new InvalidOperationException($"Artifact directory '{inProc8ArtifactDirPath}' not found!");
                }

                // Create a new directory to store the custom host with in-proc8 and in-proc6 files.
                var artifactDirName = Path.GetFileName(inProc8ArtifactDirPath);
                var consolidatedArtifactDirPath = Path.Combine(customHostTargetArtifactDir, artifactDirName);
                Directory.CreateDirectory(consolidatedArtifactDirPath);

                // Copy in-proc8 files
                var inProc8CopyTask = Task.Run(() => FileUtilities.CopyDirectory(inProc8ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, InProc8DirectoryName)));

                // Copy in-proc6 files
                var inProc6ArtifactDirPath = Path.Combine(_inProc6ExtractedRootDir, artifactDirName);
                EnsureArtifactDirectoryExist(inProc6ArtifactDirPath);
                var inProc6CopyTask = Task.Run(() => FileUtilities.CopyDirectory(inProc6ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, InProc6DirectoryName)));

                // Copy core-tools-host files
                var rid = GetRuntimeIdentifierForArtifactName(artifactName);
                var coreToolsHostArtifactDirPath = Path.Combine(_coreToolsHostExtractedRootDir, rid);
                EnsureArtifactDirectoryExist(coreToolsHostArtifactDirPath);
                var coreToolsHostCopyTask = Task.Run(() => FileUtilities.CopyDirectory(coreToolsHostArtifactDirPath, consolidatedArtifactDirPath));

                await Task.WhenAll(inProc8CopyTask, inProc6CopyTask, coreToolsHostCopyTask);

                // consolidatedArtifactDirPath now contains custom core-tools host, in-proc6 and in-proc8 sub directories. Create a zip file.
                var zipPath = Path.Combine(customHostTargetArtifactDir, $"{artifactDirName}.zip");
                await Task.Run(() => FileUtilities.CreateZipFile(consolidatedArtifactDirPath, zipPath));
                Console.WriteLine($"Created target runtime zip: {zipPath}");

                Directory.Delete(consolidatedArtifactDirPath, true);
            });

            await Task.WhenAll(packTasks);

            // Delete the extracted directories
            Directory.Delete(_inProc6ExtractedRootDir, true);
            Directory.Delete(_inProc8ExtractedRootDir, true);
            Directory.Delete(_coreToolsHostExtractedRootDir, true);
        }

        private string GetRuntimeIdentifierForArtifactName(string artifactName)
        {
            if (_customHostArtifacts.TryGetValue(artifactName, out var rid))
            {
                return rid;
            }

            throw new InvalidOperationException($"Runtime identifier (RID) not found for artifact name '{artifactName}'.");
        }

        private static async Task ExtractZipFilesInDirectoryAsync(string zipSourceDir, string extractDestinationDir)
        {
            if (!Directory.Exists(zipSourceDir))
            {
                Console.WriteLine($"Directory {zipSourceDir} does not exist.");
                return;
            }

            var zipFiles = Directory.GetFiles(zipSourceDir, "*.zip");
            Console.WriteLine($"{zipFiles.Length} zip files found in {zipSourceDir}");

            var tasks = zipFiles.Select(async zipFile =>
            {
                var destinationDir = Path.Combine(extractDestinationDir, Path.GetFileNameWithoutExtension(zipFile));
                Console.WriteLine($"Extracting {zipFile} to {destinationDir}");
                await Task.Run(() => FileUtilities.ExtractToDirectory(zipFile, destinationDir));
            });

            await Task.WhenAll(tasks);

            // Delete the zip files
            foreach (var zipFile in zipFiles)
            {
                File.Delete(zipFile);
            }
        }
    }
}

