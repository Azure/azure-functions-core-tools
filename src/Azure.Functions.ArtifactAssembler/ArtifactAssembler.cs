// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.RegularExpressions;

namespace Azure.Functions.ArtifactAssembler
{
    internal sealed class ArtifactAssembler
    {
        /// <summary>
        /// The artifacts for which we want to pack a custom host with it.
        /// This dictionary contains the artifact name and the corresponding runtime identifier value.
        /// </summary>
        private readonly Dictionary<string, string> _visualStudioArtifacts = new()
        {
            { "Azure.Functions.Cli.min.win-x64", "win-x64" },
            { "Azure.Functions.Cli.min.win-arm64", "win-arm64" },
            { "Azure.Functions.Cli.linux-x64", "linux-x64" },
            { "Azure.Functions.Cli.osx-x64", "osx-x64" },
            { "Azure.Functions.Cli.osx-arm64", "osx-arm64" }
        };

        /// <summary>
        /// The artifacts for which we want to pack out-of-proc core tools with it (along with inproc6 and inproc8 directories).
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
        private readonly string _coreToolsHostWindowsArtifactName;
        private readonly string _coreToolsHostLinuxArtifactName;
        private readonly string _coreToolsHostMacArtifactName;
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
            _coreToolsHostWindowsArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostWindowsArtifactName);
            _coreToolsHostLinuxArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostLinuxArtifactName);
            _coreToolsHostMacArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostOsxArtifactName);
            _outOfProcArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.OutOfProcArtifactName);

            _rootWorkingDirectory = rootWorkingDirectory;
            _stagingDirectory = CreateStagingDirectory(_rootWorkingDirectory);
        }

        internal async Task AssembleArtifactsAsync()
        {
            await ExtractDownloadedArtifactsAsync();
            await CreateCliCoreToolsAsync();
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
            var outOfProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _outOfProcArtifactDirectoryName);

            var inProc6ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc6ArtifactName);
            var inProc8ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc8ArtifactName);
            var outOfProcArtifactDirPath = Path.Combine(outOfProcArtifactDownloadDir, _outOfProcArtifactName);

            var coreToolsHostWindowsArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostWindowsArtifactName);
            var coreToolsHostLinuxArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostLinuxArtifactName);
            var coreToolsHostMacArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostMacArtifactName);

            EnsureArtifactDirectoryExist(inProc6ArtifactDirPath);
            EnsureArtifactDirectoryExist(inProc8ArtifactDirPath);
            EnsureArtifactDirectoryExist(outOfProcArtifactDirPath);
            EnsureArtifactDirectoryExist(coreToolsHostWindowsArtifactDirPath);
            EnsureArtifactDirectoryExist(coreToolsHostLinuxArtifactDirPath);
            EnsureArtifactDirectoryExist(coreToolsHostMacArtifactDirPath);

            _inProc6ExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(inProc6ArtifactDirPath, Path.Combine(_stagingDirectory, Constants.InProc6DirectoryName));
            _inProc8ExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(inProc8ArtifactDirPath, Path.Combine(_stagingDirectory, Constants.InProc8DirectoryName));

            Directory.Delete(inProcArtifactDownloadDir, true);

            _coreToolsHostExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(coreToolsHostWindowsArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName));
            await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(coreToolsHostLinuxArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName));
            await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(coreToolsHostMacArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName));
            Directory.Delete(coreToolsHostArtifactDownloadDir, true);

            _outOfProcExtractedRootDir = await MoveArtifactsToStagingDirectoryAndExtractIfNeeded(outOfProcArtifactDirPath, Path.Combine(_stagingDirectory, Constants.OutOfProcDirectoryName));
            Directory.Delete(outOfProcArtifactDownloadDir, true);
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
            var stagingDirectory = Path.Combine(rootWorkingDirectory, Constants.StagingDirName);

            if (Directory.Exists(stagingDirectory))
            {
                Console.WriteLine($"Directory '{stagingDirectory}' already exists, deleting...");
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

            // Delete additional files that are not needed
            var filesToBeDeleted = Directory.EnumerateFiles(destinationDirectory);
            foreach (var file in filesToBeDeleted)
            {
                File.Delete(file);
            }
            return destinationDirectory;
        }

        // Gets the product version part from the artifact directory name.
        // Example input: Azure.Functions.Cli.min.win-x64.4.0.6353
        // Example output: 4.0.6353
        private static string GetCoreToolsProductVersion(string artifactDirectoryName)
        {
            var match = Regex.Match(artifactDirectoryName, Constants.CoreToolsProductVersionPattern);
            if (match.Success)
            {
                return match.Value;
            }

            throw new InvalidOperationException($"The artifact directory name '{artifactDirectoryName}' does not include a core tools product version in the expected format (e.g., '4.0.6353'). Please ensure the directory name follows the correct naming convention.");
        }

        private async Task CreateVisualStudioCoreToolsAsync()
        {
            Console.WriteLine("Starting to assemble Visual Studio Core Tools artifacts");
            // Create a directory to store the assembled artifacts.
            var customHostTargetArtifactDir = Path.Combine(_stagingDirectory, Constants.VisualStudioOutputArtifactDirectoryName);
            Directory.CreateDirectory(customHostTargetArtifactDir);

            foreach (string artifactName in _visualStudioArtifacts.Keys)
            {
                var inProc8ArtifactDirPath = Directory.EnumerateDirectories(_inProc8ExtractedRootDir)
                                          .FirstOrDefault(dir => dir.Contains(artifactName));
                if (inProc8ArtifactDirPath == null)
                {
                    throw new InvalidOperationException($"Artifact directory '{inProc8ArtifactDirPath}' not found!");
                }

                // Create a new directory to store the custom host with in-proc8 and in-proc6 files.
                var artifactDirName = Path.GetFileName(inProc8ArtifactDirPath);
                var consolidatedArtifactDirName = $"{artifactName}{Constants.InProcOutputArtifactNameSuffix}.{GetCoreToolsProductVersion(artifactDirName)}";
                var consolidatedArtifactDirPath = Path.Combine(customHostTargetArtifactDir, consolidatedArtifactDirName);
                Directory.CreateDirectory(consolidatedArtifactDirPath);

                // Copy in-proc8 files and delete directory after
                await Task.Run(() => FileUtilities.CopyDirectory(inProc8ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, Constants.InProc8DirectoryName)));
                Directory.Delete(inProc8ArtifactDirPath, true);

                // Copy in-proc6 files and delete directory after
                var inProc6ArtifactDirPath = Path.Combine(_inProc6ExtractedRootDir, artifactDirName);
                EnsureArtifactDirectoryExist(inProc6ArtifactDirPath);
                await Task.Run(() => FileUtilities.CopyDirectory(inProc6ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, Constants.InProc6DirectoryName)));
                Directory.Delete(inProc6ArtifactDirPath, true);

                // Copy core-tools-host files
                var rid = GetRuntimeIdentifierForArtifactName(artifactName);
                var coreToolsHostArtifactDirPath = Path.Combine(_coreToolsHostExtractedRootDir, rid);
                EnsureArtifactDirectoryExist(coreToolsHostArtifactDirPath);
                await Task.Run(() => FileUtilities.CopyDirectory(coreToolsHostArtifactDirPath, consolidatedArtifactDirPath));
                Directory.Delete(coreToolsHostArtifactDirPath, true);

                // consolidatedArtifactDirPath now contains custom core-tools host, in-proc6 and in-proc8 sub directories. Create a zip file.
                var zipPath = Path.Combine(customHostTargetArtifactDir, $"{consolidatedArtifactDirName}.zip");
                await Task.Run(() => FileUtilities.CreateZipFile(consolidatedArtifactDirPath, zipPath));
                Console.WriteLine($"Successfully created target runtime zip at: {zipPath}");

                Directory.Delete(consolidatedArtifactDirPath, true);
            }

            // Delete directories
            Directory.Delete(_inProc6ExtractedRootDir, true);
            Directory.Delete(_inProc8ExtractedRootDir, true);
            Directory.Delete(_coreToolsHostExtractedRootDir, true);

            Console.WriteLine("Finished assembling Visual Studio Core Tools artifacts");
        }

        private async Task CreateCliCoreToolsAsync()
        {
            Console.WriteLine("Starting to assemble CLI Core Tools artifacts");

            // Create a directory to store the assembled artifacts.
            var cliCoreToolsTargetArtifactDir = Path.Combine(_stagingDirectory, Constants.CliOutputArtifactDirectoryName);
            Directory.CreateDirectory(cliCoreToolsTargetArtifactDir);
            string outOfProcVersion = string.Empty,
                   inProcVersion = string.Empty,
                   outOfProcArtifactDirPath = string.Empty,
                   inProc8ArtifactDirPath = string.Empty;

            foreach (var artifactName in _cliArtifacts)
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

                // Copy oop core tools and delete old directory
                EnsureArtifactDirectoryExist(outOfProcArtifactDirPath);
                await Task.Run(() => FileUtilities.CopyDirectory(outOfProcArtifactDirPath, consolidatedArtifactDirPath));
                Directory.Delete(outOfProcArtifactDirPath, true);

                // If we are currently on the minified version of the artifacts, we do not want the inproc6/inproc8 subfolders
                if (artifactName.Contains("min.win"))
                {
                    Console.WriteLine($"Finished assembling {consolidatedArtifactDirPath}");
                    Console.WriteLine();
                    continue;
                }

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

                // Copy in-proc8 files
                var inProc8FinalDestination = Path.Combine(consolidatedArtifactDirPath, Constants.InProc8DirectoryName);
                await Task.Run(() => FileUtilities.CopyDirectory(inProc8ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, Constants.InProc8DirectoryName)));
                Console.WriteLine($"Copied files from {inProc8ArtifactDirPath} => {inProc8FinalDestination}");

                // Rename inproc6 directory to have the same version as the out-of-proc artifact before copying
                var inProcArtifactName = Path.GetFileName(inProc8ArtifactDirPath);
                var inProc6ArtifactDirPath = Path.Combine(_inProc6ExtractedRootDir, inProcArtifactName);
                EnsureArtifactDirectoryExist(inProc6ArtifactDirPath);

                // Copy in-proc6 files
                var inProc6FinalDestination = Path.Combine(consolidatedArtifactDirPath, Constants.InProc6DirectoryName);
                await Task.Run(() => FileUtilities.CopyDirectory(inProc6ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, Constants.InProc6DirectoryName)));
                Console.WriteLine($"Copied files from {inProc8ArtifactDirPath} => {inProc8FinalDestination}");

                Console.WriteLine($"Finished assembling {consolidatedArtifactDirPath}");
                Console.WriteLine();
            }

            // Delete the extracted directories
            Directory.Delete(_outOfProcExtractedRootDir, true);

            Console.WriteLine("Finished assembling CLI Core Tools artifacts");
            Console.WriteLine();
        }

        private (string artifactDirectory, string version) GetArtifactDirectoryAndVersionNumber(string extractedRootDirectory, string artifactName)
        {
            var artifactDirPath = Directory.EnumerateDirectories(extractedRootDirectory)
                                           .FirstOrDefault(dir => dir.Contains(artifactName));
            if (artifactDirPath is null)
            {
                throw new InvalidOperationException($"Artifact directory '{artifactDirPath}' not found!");
            }

            var version = GetCoreToolsProductVersion(artifactDirPath);
            return (artifactDirPath, version);
        }

        private string GetRuntimeIdentifierForArtifactName(string artifactName)
        {
            if (_visualStudioArtifacts.TryGetValue(artifactName, out var rid))
            {
                return rid;
            }

            throw new InvalidOperationException($"Runtime identifier (RID) not found for artifact name '{artifactName}'.");
        }

        private static async Task ExtractZipFilesInDirectoryAsync(string zipSourceDir, string extractDestinationDir)
        {
            if (!Directory.Exists(zipSourceDir))
            {
                Console.WriteLine("Directory {zipSourceDir} does not exist.");
                return;
            }

            var zipFiles = Directory.GetFiles(zipSourceDir, "*.zip");
            Console.WriteLine($"{zipFiles.Length} zip files found in {zipSourceDir}");

            // Extract each zip file and delete
            foreach (var zipFile in zipFiles)
            {
                var destinationDir = Path.Combine(extractDestinationDir, Path.GetFileNameWithoutExtension(zipFile));
                Console.WriteLine($"Extracting {zipFile} to {destinationDir}");
                await Task.Run(() => FileUtilities.ExtractToDirectory(zipFile, destinationDir));
                File.Delete(zipFile);
            }
        }
    }
}