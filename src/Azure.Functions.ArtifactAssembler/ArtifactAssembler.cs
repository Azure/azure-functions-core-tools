// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.RegularExpressions;

namespace Azure.Functions.ArtifactAssembler
{
    internal sealed class ArtifactAssembler
    {
        private const string StagingDirName = "staging";
        private const string InProc8DirectoryName = "in-proc8";
        private const string InProc6DirectoryName = "in-proc6";
        private const string CoreToolsHostDirectoryName = "host";
        private const string VisualStudioOutputArtifactDirectoryName = "coretools-visualstudio";
        private const string _InProcOutputArtifactNameSuffix = "_inproc";
        private const string _coreToolsProductVersionPattern = @"(\d+\.\d+\.\d+)$";
        private const string OutOfProcDirectoryName = "out-of-proc";
        private const string CliOutputArtifactDirectoryName = "coretools-cli";

        /// <summary>
        /// The artifacts for which we want to pack a custom host with it.
        /// This dictionary contains the artifact name and the corresponding runtime identifier value.
        /// </summary>
        private readonly Dictionary<string, string> _customHostArtifacts = new()
        {
            { "Azure.Functions.Cli.min.win-x64", "win-x64" },
            { "Azure.Functions.Cli.min.win-arm64", "win-arm64" }
        };

        /// <summary>
        /// The artifacts for which we want to pack out-of-proc core tools with it (along with inproc6 and inproc8 directories).
        /// This dictionary contains the artifact name and the corresponding runtime identifier value.
        /// </summary>
        private readonly string[] _cliArtifacts =
        {
            "Azure.Functions.Cli.min.win-arm64",
            "Azure.Functions.Cli.min.win-x86",
            "Azure.Functions.Cli.min.win-x64",
            "Azure.Functions.Cli.linux-x64",
            "Azure.Functions.Cli.osx-x64",
            "Azure.Functions.Cli.osx-arm64",
            "Azure.Functions.Cli.win-x86",
            "Azure.Functions.Cli.win-x64",
            "Azure.Functions.Cli.win-arm64"
        };

        private readonly string _inProcArtifactDirectoryName;
        private readonly string _coreToolsHostArtifactDirectoryName;
        private readonly string _outOfProcArtifactDirectoryName;
        private readonly string _inProc6ArtifactName;
        private readonly string _inProc8ArtifactName;
        private readonly string _coreToolsHostArtifactName;
        private readonly string _outOfProcArtifactName;
        private readonly string _rootWorkingDirectory;
        private readonly string _stagingDirectory;

        private string _inProc6ExtractedRootDir = string.Empty;
        private string _inProc8ExtractedRootDir = string.Empty;
        private string _coreToolsHostExtractedRootDir = string.Empty;
        private string _outOfProcExtractedRootDir = string.Empty;

        internal ArtifactAssembler(string rootWorkingDirectory)
        {
            _inProcArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProcArtifactAlias);
            _coreToolsHostArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostArtifactAlias);
            _outOfProcArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.OutOfProcArtifactAlias);

            _inProc6ArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProc6ArtifactName);
            _inProc8ArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProc8ArtifactName);
            _coreToolsHostArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostArtifactName);
            _outOfProcArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.OutOfProcArtifactName);

            _rootWorkingDirectory = rootWorkingDirectory;
            _stagingDirectory = CreateStagingDirectory(_rootWorkingDirectory);
        }

        internal async Task AssembleArtifactsVisualStudioAsync()
        {
            await ExtractDownloadedArtifactsAsync();
            await CreateVisualStudioCoreToolsAsync();
            // await CreateCliCoreToolsAsync();
        }

        internal async Task AssembleArtifactsCliAsync()
        {
            var outOfProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _outOfProcArtifactDirectoryName);
            var outOfProcArtifactDirPath = Path.Combine(outOfProcArtifactDownloadDir, _outOfProcArtifactName);
            EnsureArtifactDirectoryExist(outOfProcArtifactDirPath);
            _outOfProcExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(outOfProcArtifactDirPath, Path.Combine(_stagingDirectory, OutOfProcDirectoryName));
            Directory.Delete(outOfProcArtifactDownloadDir, true);
            //await CreateVisualStudioCoreToolsAsync();
            await CreateCliCoreToolsAsync();
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
            //var outOfProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _outOfProcArtifactDirectoryName);

            var inProc6ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc6ArtifactName);
            var inProc8ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc8ArtifactName);
            var coreToolsHostArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostArtifactName);

            EnsureArtifactDirectoryExist(inProc6ArtifactDirPath);
            EnsureArtifactDirectoryExist(inProc8ArtifactDirPath);
            EnsureArtifactDirectoryExist(coreToolsHostArtifactDirPath);
           // EnsureArtifactDirectoryExist(outOfProcArtifactDirPath);

            _inProc6ExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(inProc6ArtifactDirPath, Path.Combine(_stagingDirectory, InProc6DirectoryName));
            _inProc8ExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(inProc8ArtifactDirPath, Path.Combine(_stagingDirectory, InProc8DirectoryName));

            Directory.Delete(inProcArtifactDownloadDir, true);

            _coreToolsHostExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(coreToolsHostArtifactDirPath, Path.Combine(_stagingDirectory, CoreToolsHostDirectoryName));
            Directory.Delete(coreToolsHostArtifactDownloadDir, true);
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
                Console.WriteLine($"Directory already exists");
                return stagingDirectory;
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

        // Gets the product version part from the artifact directory name.
        // Example input: Azure.Functions.Cli.min.win-x64.4.0.6353
        // Example output: 4.0.6353
        private static string GetCoreToolsProductVersion(string artifactDirectoryName)
        {
            var match = Regex.Match(artifactDirectoryName, _coreToolsProductVersionPattern);
            if (match.Success)
            {
                return match.Value;
            }

            throw new InvalidOperationException($"The artifact directory name '{artifactDirectoryName}' does not include a core tools product version in the expected format (e.g., '4.0.6353'). Please ensure the directory name follows the correct naming convention.");
        }

        private async Task CreateVisualStudioCoreToolsAsync()
        {
            Console.WriteLine($"Starting to assemble Visual Studio Core Tools artifacts");
            // Create a directory to store the assembled artifacts.
            var customHostTargetArtifactDir = Path.Combine(_stagingDirectory, VisualStudioOutputArtifactDirectoryName);
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
                var consolidatedArtifactDirName = $"{artifactName}{_InProcOutputArtifactNameSuffix}.{GetCoreToolsProductVersion(artifactDirName)}";
                var consolidatedArtifactDirPath = Path.Combine(customHostTargetArtifactDir, consolidatedArtifactDirName);
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
                var zipPath = Path.Combine(customHostTargetArtifactDir, $"{consolidatedArtifactDirName}.zip");
                await Task.Run(() => FileUtilities.CreateZipFile(consolidatedArtifactDirPath, zipPath));
                Console.WriteLine($"Successfully created target runtime zip at: {zipPath}");

                Directory.Delete(consolidatedArtifactDirPath, true);
            });

            await Task.WhenAll(packTasks);
            Console.WriteLine($"Finished assembling Visual Studio Core Tools artifacts");
        }

        private async Task CreateCliCoreToolsAsync()
        {
            Console.WriteLine($"Starting to assemble CLI Core Tools artifacts");

            // Create a directory to store the assembled artifacts.
            var cliCoreToolsTargetArtifactDir = Path.Combine(_stagingDirectory, CliOutputArtifactDirectoryName);
            Directory.CreateDirectory(cliCoreToolsTargetArtifactDir);
            string outOfProcVersion = string.Empty;
            string inProcVersion = string.Empty;
            string outOfProcArtifactDirPath = string.Empty;
            string inProc8ArtifactDirPath = string.Empty;

            var packTasks = _cliArtifacts.Select(async artifactName =>
            {
                // If we are running this for the first time, extract the directory path and out of proc version
                if (String.IsNullOrEmpty(outOfProcArtifactDirPath))
                {
                    var outOfProcResponse = GetArtifactDirectoryAndVersionNumber(_outOfProcExtractedRootDir, artifactName);
                    outOfProcArtifactDirPath = outOfProcResponse.artifactDirectory;
                    outOfProcVersion = outOfProcResponse.version;
                }
                else
                {
                    var artifactNameWithVersion = $"{artifactName}.{outOfProcVersion}";
                    outOfProcArtifactDirPath = Path.Combine(_outOfProcExtractedRootDir, artifactNameWithVersion);
                }

                // Create a new directory to store the oop core tools with in-proc8 and in-proc6 files.
                var outOfProcArtifactName = Path.GetFileName(outOfProcArtifactDirPath);
                var consolidatedArtifactDirPath = Path.Combine(cliCoreToolsTargetArtifactDir, outOfProcArtifactName);
                Directory.CreateDirectory(consolidatedArtifactDirPath);

                // Copy oop core tools
                var coreToolsHostArtifactDirPath = Path.Combine(_outOfProcExtractedRootDir, outOfProcArtifactName);
                EnsureArtifactDirectoryExist(coreToolsHostArtifactDirPath);
                var outOfProcCopyTask = Task.Run(() => FileUtilities.CopyDirectory(coreToolsHostArtifactDirPath, consolidatedArtifactDirPath));

                // If we are running this for the first time, extract the directory path and out of proc version
                if (String.IsNullOrEmpty(inProc8ArtifactDirPath))
                {
                    // Get the version number from the in-proc build since it will be different than out-of-proc
                    var inProc8Response = GetArtifactDirectoryAndVersionNumber(_inProc8ExtractedRootDir, artifactName);
                    inProc8ArtifactDirPath = inProc8Response.artifactDirectory;
                    inProcVersion = inProc8Response.version;
                }
                else
                {
                    var artifactNameWithVersion = $"{artifactName}.{inProcVersion}";
                    inProc8ArtifactDirPath = Path.Combine(_inProc8ExtractedRootDir, artifactNameWithVersion);
                }

                // Rename inproc8 directory to have the same version as the out-of-proc artifact before copying
                string newInProc8ArtifactDirPath = RenameInProcDirectory(inProc8ArtifactDirPath, outOfProcVersion);

                // Copy in-proc8 files
                var inProc8CopyTask = Task.Run(() => FileUtilities.CopyDirectory(newInProc8ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, InProc8DirectoryName)));

                // Rename inproc6 directory to have the same version as the out-of-proc artifact before copying
                var inProcArtifactName = Path.GetFileName(inProc8ArtifactDirPath);
                var inProc6ArtifactDirPath = Path.Combine(_inProc6ExtractedRootDir, inProcArtifactName);
                EnsureArtifactDirectoryExist(inProc6ArtifactDirPath);
                string newInProc6ArtifactDirPath = RenameInProcDirectory(inProc6ArtifactDirPath, outOfProcVersion);

                // Copy in-proc6 files
                var inProc6CopyTask = Task.Run(() => FileUtilities.CopyDirectory(newInProc6ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, InProc6DirectoryName)));

                await Task.WhenAll(inProc8CopyTask, inProc6CopyTask, outOfProcCopyTask);

                // consolidatedArtifactDirPath now contains custom core-tools host, in-proc6 and in-proc8 sub directories. Create a zip file.
                var zipPath = Path.Combine(cliCoreToolsTargetArtifactDir, $"{outOfProcArtifactName}.zip");
                try
                {
                    await Task.Run(() => FileUtilities.CreateZipFile(consolidatedArtifactDirPath, zipPath));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
                Console.WriteLine($"Successfully created target runtime zip at: {zipPath}");

                Directory.Delete(consolidatedArtifactDirPath, true);
            });

            await Task.WhenAll(packTasks);

            // Delete the extracted directories
            Directory.Delete(_inProc6ExtractedRootDir, true);
            Directory.Delete(_inProc8ExtractedRootDir, true);
            Directory.Delete(_coreToolsHostExtractedRootDir, true);
            Directory.Delete(_outOfProcExtractedRootDir, true);

            Console.WriteLine($"Finished assembling CLI Core Tools artifacts");
        }

        private (string artifactDirectory, string version) GetArtifactDirectoryAndVersionNumber(string extractedRootDirectory, string artifactName)
        {
            var artifactDirPath = Directory.EnumerateDirectories(extractedRootDirectory)
                                          .FirstOrDefault(dir => dir.Contains(artifactName));
            if (artifactDirPath == null)
            {
                throw new InvalidOperationException($"Artifact directory '{artifactDirPath}' not found!");
            }

            var version = GetCoreToolsProductVersion(artifactDirPath);
            return (artifactDirPath, version);
        }

        private string RenameInProcDirectory(string oldArtifactDirPath, string newVersion)
        {
            string pattern = @"^(.*?)(\d+\.\d+\.\d+)$";  // Capture everything before the version

            Match match = Regex.Match(oldArtifactDirPath, pattern);

            if (!match.Success)
            {
                throw new InvalidOperationException($"Unable to extract content before version number from '{oldArtifactDirPath}'.");
            }

            var contentBeforeVersion = match.Groups[1];
            var newDirectoryName =  $"{contentBeforeVersion}{newVersion}";

            // Rename (move) the directory
            try
            {
                Directory.Move(oldArtifactDirPath, newDirectoryName);
            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }
            return newDirectoryName;

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

