// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.ArtifactAssembler
{
    internal sealed partial class ArtifactAssembler
    {
        /// <summary>
        /// The artifacts for which we want to pack a custom host with it.
        /// This dictionary contains the artifact name and the corresponding runtime identifier value.
        /// </summary>
        private readonly Dictionary<string, string> _visualStudioArtifacts = new()
        {
            { "Azure.Functions.Cli.min.win-x64", "win-x64" },
            { "Azure.Functions.Cli.min.win-arm64", "win-arm64" },
            { "Azure.Functions.Cli.linux-x64", "linux-x64" }
        };

        private readonly string[] _net8OsxArtifacts =
        [
            "Azure.Functions.Cli.osx-x64",
            "Azure.Functions.Cli.osx-arm64",
        ];

        /// <summary>
        /// The artifacts for which we want to pack out-of-proc core tools with it (along with inproc6 and inproc8 directories).
        /// </summary>
        private readonly string[] _cliArtifacts =
        [
            "Azure.Functions.Cli.min.win-arm64",
            "Azure.Functions.Cli.min.win-x86",
            "Azure.Functions.Cli.min.win-x64",
            "Azure.Functions.Cli.linux-x64",
            "Azure.Functions.Cli.osx-x64",
            "Azure.Functions.Cli.osx-arm64",
            "Azure.Functions.Cli.win-x86",
            "Azure.Functions.Cli.win-x64",
            "Azure.Functions.Cli.win-arm64"
        ];

        private readonly string _inProcArtifactDirectoryName;
        private readonly string _coreToolsHostArtifactDirectoryName;
        private readonly string _outOfProcArtifactDirectoryName;
        private readonly string _inProc6ArtifactName;
        private readonly string _inProc8ArtifactName;
        private readonly string _coreToolsHostWindowsArtifactName;
        private readonly string _coreToolsHostLinuxArtifactName;
        private readonly string _outOfProcArtifactName;
        private readonly string _rootWorkingDirectory;
        private readonly string _stagingDirectory;
        private readonly string _releaseDirectory;
        private readonly string _artifactName;

        private string _inProc6ExtractedRootDir = string.Empty;
        private string _inProc8ExtractedRootDir = string.Empty;
        private string _coreToolsHostExtractedRootDir = string.Empty;
        private string _outOfProcExtractedRootDir = string.Empty;

        internal ArtifactAssembler(string rootWorkingDirectory, string artifactName)
        {
            _inProcArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProcArtifactAlias);
            _coreToolsHostArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostArtifactAlias);
            _outOfProcArtifactDirectoryName = GetRequiredEnvironmentVariable(EnvironmentVariables.OutOfProcArtifactAlias);

            _inProc6ArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProc6ArtifactName);
            _inProc8ArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.InProc8ArtifactName);
            _coreToolsHostWindowsArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostWindowsArtifactName);
            _coreToolsHostLinuxArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.CoreToolsHostLinuxArtifactName);
            _outOfProcArtifactName = GetRequiredEnvironmentVariable(EnvironmentVariables.OutOfProcArtifactName);

            _rootWorkingDirectory = rootWorkingDirectory;
            _stagingDirectory = CreateDirectory(_rootWorkingDirectory, Constants.StagingDirName);
            _releaseDirectory = CreateDirectory(_rootWorkingDirectory, Constants.ReleaseDirName);
            _artifactName = artifactName;
        }

        internal void Assemble()
        {
            try
            {
                ExtractDownloadedArtifacts();
                CreateCliCoreTools();
                CreateVisualStudioCoreTools();
            }
            finally
            {
                Directory.Delete(_stagingDirectory, true);
            }
        }

        private static string GetRequiredEnvironmentVariable(string variableName)
        {
            return Environment.GetEnvironmentVariable(variableName)
                ?? throw new InvalidDataException($"The `{variableName}` environment variable value is missing!");
        }

        private void ExtractDownloadedArtifacts()
        {
            var inProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _inProcArtifactDirectoryName);
            var outOfProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _outOfProcArtifactDirectoryName);

            var inProc6ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc6ArtifactName);
            var inProc8ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc8ArtifactName);
            var outOfProcArtifactDirPath = Path.Combine(outOfProcArtifactDownloadDir, _outOfProcArtifactName);

            CheckIfArtifactDirectoryExists(inProc6ArtifactDirPath);
            CheckIfArtifactDirectoryExists(inProc8ArtifactDirPath);
            CheckIfArtifactDirectoryExists(outOfProcArtifactDirPath);

            _inProc6ExtractedRootDir = PrepareStagingDirectory(inProc6ArtifactDirPath, Path.Combine(_stagingDirectory, Constants.InProc6DirectoryName));
            _inProc8ExtractedRootDir = PrepareStagingDirectory(inProc8ArtifactDirPath, Path.Combine(_stagingDirectory, Constants.InProc8DirectoryName));
            Directory.Delete(inProcArtifactDownloadDir, true);

            _outOfProcExtractedRootDir = PrepareStagingDirectory(outOfProcArtifactDirPath, Path.Combine(_stagingDirectory, Constants.OutOfProcDirectoryName));
            Directory.Delete(outOfProcArtifactDownloadDir, true);

            SetupCoreToolsHostArtifactDirectories();
        }

        private void SetupCoreToolsHostArtifactDirectories()
        {
            var coreToolsHostArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _coreToolsHostArtifactDirectoryName);
            var coreToolsHostWindowsArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostWindowsArtifactName);
            var coreToolsHostLinuxArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostLinuxArtifactName);

            if (string.IsNullOrEmpty(_artifactName))
            {
                // No artifact name provided: run all host extractions
                if (Directory.Exists(coreToolsHostWindowsArtifactDirPath))
                {
                    CheckIfArtifactDirectoryExists(coreToolsHostWindowsArtifactDirPath);
                    _coreToolsHostExtractedRootDir = PrepareStagingDirectory(coreToolsHostWindowsArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName), true);
                }

                if (Directory.Exists(coreToolsHostLinuxArtifactDirPath))
                {
                    CheckIfArtifactDirectoryExists(coreToolsHostLinuxArtifactDirPath);
                    _ = PrepareStagingDirectory(coreToolsHostLinuxArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName), true);
                }

                Directory.Delete(coreToolsHostArtifactDownloadDir, true);
            }
            else if (_visualStudioArtifacts.TryGetValue(_artifactName, out var rid))
            {
                Console.WriteLine($"Assembling host build for artifact: {_artifactName} - RID: {rid}");
                // Specific artifact name provided and it's in the dictionary
                if (rid.StartsWith("win") && Directory.Exists(coreToolsHostWindowsArtifactDirPath))
                {
                    CheckIfArtifactDirectoryExists(coreToolsHostWindowsArtifactDirPath);
                    _coreToolsHostExtractedRootDir = PrepareStagingDirectory(coreToolsHostWindowsArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName), true);
                }
                else if (rid.StartsWith("linux") && Directory.Exists(coreToolsHostLinuxArtifactDirPath))
                {
                    CheckIfArtifactDirectoryExists(coreToolsHostLinuxArtifactDirPath);
                    _coreToolsHostExtractedRootDir = PrepareStagingDirectory(coreToolsHostLinuxArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName), true);
                }

                Directory.Delete(coreToolsHostArtifactDownloadDir, true);
            }
            else
            {
                Console.WriteLine($"No host build required for artifact: {_artifactName}");
            }
        }

        private static void CheckIfArtifactDirectoryExists(string directoryExist)
        {
            if (!Directory.Exists(directoryExist))
            {
                throw new InvalidOperationException($"Artifact directory '{directoryExist}' not found!");
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static string CreateDirectory(string rootWorkingDirectory, string dirName)
        {
            var directory = Path.Combine(rootWorkingDirectory, dirName);

            if (Directory.Exists(directory))
            {
                Console.WriteLine($"Directory '{directory}' already exists, deleting...");
                Directory.Delete(directory, true);
            }

            Directory.CreateDirectory(directory);
            Console.WriteLine($"Created {dirName} directory: {directory}");

            return directory;
        }

        private string PrepareStagingDirectory(string artifactZipPath, string destinationDirectory, bool isCoreToolsHost = false)
        {
            EnsureDirectoryExists(destinationDirectory);

            if (!string.IsNullOrEmpty(_artifactName))
            {
                if (isCoreToolsHost)
                {
                    CopyCoreToolsArtifactIfExists(artifactZipPath, destinationDirectory);
                    return destinationDirectory;
                }

                ExtractZipFilesInDirectory(artifactZipPath, destinationDirectory);
            }
            else
            {
                FileUtilities.CopyDirectory(artifactZipPath, destinationDirectory);
                ExtractZipFilesInDirectory(artifactZipPath, destinationDirectory);
            }

            DeleteUnnecessaryFiles(destinationDirectory);
            return destinationDirectory;
        }

        private void CopyCoreToolsArtifactIfExists(string artifactZipPath, string destinationDirectory)
        {
            if (_visualStudioArtifacts.TryGetValue(_artifactName, out var fileNameToSearchFor) &&
                !string.IsNullOrEmpty(fileNameToSearchFor))
            {
                var sourceDir = Path.Combine(artifactZipPath, fileNameToSearchFor);
                var destDir = Path.Combine(destinationDirectory, fileNameToSearchFor);

                if (Directory.Exists(sourceDir))
                {
                    FileUtilities.CopyDirectory(sourceDir, destDir);
                }
            }
        }

        private static void DeleteUnnecessaryFiles(string directory)
        {
            var filesToBeDeleted = Directory.EnumerateFiles(directory);
            foreach (var file in filesToBeDeleted)
            {
                File.Delete(file);
            }
        }

        // Gets the product version part from the artifact directory name.
        // Example input: Azure.Functions.Cli.min.win-x64.4.0.6353
        // Example output: 4.0.6353
        private static string GetCoreToolsProductVersion(string artifactDirectoryName)
        {
            var match = CoreToolsVersionRegex().Match(artifactDirectoryName);
            if (match.Success)
            {
                return match.Value;
            }

            throw new InvalidOperationException($"The artifact directory name '{artifactDirectoryName}' does not include a core tools product version in the expected format (e.g., '4.0.6353'). Please ensure the directory name follows the correct naming convention.");
        }

        private void CreateVisualStudioCoreTools()
        {
            Console.WriteLine("Starting to assemble Visual Studio Core Tools artifacts");

            bool artifactNameProvided = !string.IsNullOrEmpty(_artifactName);
            bool isValidVisualStudioArtifact = _visualStudioArtifacts.ContainsKey(_artifactName);

            // Create a directory to store the assembled artifacts.
            var customHostTargetArtifactDir = Path.Combine(_releaseDirectory, Constants.VisualStudioOutputArtifactDirectoryName);
            Directory.CreateDirectory(customHostTargetArtifactDir);

            string[] visualStudioArtifactList = artifactNameProvided ? [_artifactName] : [.. _visualStudioArtifacts.Keys];

            foreach (string artifactName in visualStudioArtifactList)
            {
                // Break early if we don't need to assemble VS artifacts for specified artifactName
                if (artifactNameProvided && !isValidVisualStudioArtifact)
                {
                    break;
                }

                var (artifactDirName, consolidatedArtifactDirPath) = CreateInProc8CoreToolsHostHelper(artifactName, customHostTargetArtifactDir, createDirectory: true);

                // Copy in-proc6 files and delete directory after
                var inProc6ArtifactDirPath = Directory
                    .EnumerateDirectories(_inProc6ExtractedRootDir)
                    .FirstOrDefault(dir => dir.Contains(artifactName))
                    ?? throw new InvalidOperationException($"Artifact directory for '{artifactName}' not found.");

                CheckIfArtifactDirectoryExists(inProc6ArtifactDirPath);
                FileUtilities.CopyDirectory(inProc6ArtifactDirPath, Path.Combine(consolidatedArtifactDirPath, Constants.InProc6DirectoryName));
                Directory.Delete(inProc6ArtifactDirPath, true);

                // Copy core-tools-host files
                var rid = GetRuntimeIdentifierForArtifactName(artifactName);
                var coreToolsHostArtifactDirPath = Path.Combine(_coreToolsHostExtractedRootDir, rid);
                CheckIfArtifactDirectoryExists(coreToolsHostArtifactDirPath);
                FileUtilities.CopyDirectory(coreToolsHostArtifactDirPath, consolidatedArtifactDirPath);
                Directory.Delete(coreToolsHostArtifactDirPath, true);
            }

            // Generate .NET 8 OSX fallback artifacts
            if (artifactNameProvided && _net8OsxArtifacts.Contains(_artifactName))
            {
                CreateInProc8CoreToolsHostHelper(_artifactName, customHostTargetArtifactDir, createDirectory: false);
            }
            else if (!artifactNameProvided)
            {
                // Create artifacts for .NET 8 OSX to use instead of the custom host
                foreach (var osxArtifact in _net8OsxArtifacts)
                {
                    CreateInProc8CoreToolsHostHelper(osxArtifact, customHostTargetArtifactDir, createDirectory: false);
                }
            }

            // Delete directories
            Directory.Delete(_inProc6ExtractedRootDir, true);
            Directory.Delete(_inProc8ExtractedRootDir, true);

            Console.WriteLine("Finished assembling Visual Studio Core Tools artifacts");
        }

        // This method creates a new directory for the core tools host and copies the inproc8 files
        private (string ArtifactDirName, string ConsolidatedArtifactDirPath) CreateInProc8CoreToolsHostHelper(
            string artifactName, string customHostTargetArtifactDir, bool createDirectory)
        {
            var inProcArtifactDirPath = Directory
                .EnumerateDirectories(_inProc8ExtractedRootDir)
                .FirstOrDefault(dir => dir.Contains(artifactName))
                ?? throw new InvalidOperationException($"Artifact directory for '{artifactName}' not found.");

            // Create a new directory to store the custom host.
            var artifactDirName = Path.GetFileName(inProcArtifactDirPath);
            var version = GetCoreToolsProductVersion(artifactDirName);
            var consolidatedArtifactDirName = $"{artifactName}{Constants.InProcOutputArtifactNameSuffix}.{version}";
            var consolidatedArtifactDirPath = Path.Combine(customHostTargetArtifactDir, consolidatedArtifactDirName);
            Directory.CreateDirectory(consolidatedArtifactDirPath);

            // Copy in-proc8 files and delete directory after
            var copyTargetPath = createDirectory
                ? Path.Combine(consolidatedArtifactDirPath, Constants.InProc8DirectoryName)
                : consolidatedArtifactDirPath;

            FileUtilities.CopyDirectory(inProcArtifactDirPath, copyTargetPath);
            Directory.Delete(inProcArtifactDirPath, true);

            return (artifactDirName, consolidatedArtifactDirPath);
        }

        private void CreateCliCoreTools()
        {
            Console.WriteLine("Starting to assemble CLI Core Tools artifacts");

            // Create a directory to store the assembled artifacts.
            var cliCoreToolsTargetArtifactDir = Path.Combine(_releaseDirectory, Constants.CliOutputArtifactDirectoryName);
            Directory.CreateDirectory(cliCoreToolsTargetArtifactDir);

            string cliVersion = string.Empty,
                   outOfProcArtifactDirPath = string.Empty,
                   inProc6ArtifactDirPath = string.Empty,
                   inProc8ArtifactDirPath = string.Empty;

            string[] cliArtifactList = string.IsNullOrEmpty(_artifactName) ? _cliArtifacts : [_artifactName];

            foreach (var artifactName in cliArtifactList)
            {
                // Set up out-of-proc artifact directory and version
                if (string.IsNullOrEmpty(outOfProcArtifactDirPath))
                {
                    var (artifactDirectory, version) = GetArtifactDirectoryAndVersionNumber(_outOfProcExtractedRootDir, artifactName);
                    outOfProcArtifactDirPath = artifactDirectory;
                    cliVersion = version;
                }
                else
                {
                    outOfProcArtifactDirPath = Path.Combine(_outOfProcExtractedRootDir, $"{artifactName}.{cliVersion}");
                }

                // Create a new directory to store the oop core tools with in-proc8 and in-proc6 files.
                var consolidatedArtifactDirPath = Path.Combine(cliCoreToolsTargetArtifactDir, Path.GetFileName(outOfProcArtifactDirPath));
                Directory.CreateDirectory(consolidatedArtifactDirPath);

                // Copy oop core tools and delete old directory
                CheckIfArtifactDirectoryExists(outOfProcArtifactDirPath);
                FileUtilities.CopyDirectory(outOfProcArtifactDirPath, consolidatedArtifactDirPath);
                Directory.Delete(outOfProcArtifactDirPath, true);

                // If we are currently on the minified version of the artifacts, we do not want the inproc6/inproc8 subfolders
                if (artifactName.Contains("min.win"))
                {
                    Console.WriteLine($"Finished assembling {consolidatedArtifactDirPath}\n");
                    continue;
                }

                if (string.IsNullOrEmpty(inProc6ArtifactDirPath) || string.IsNullOrEmpty(inProc8ArtifactDirPath))
                {
                    inProc8ArtifactDirPath = Path.Combine(_inProc8ExtractedRootDir, $"{artifactName}.inproc8.{cliVersion}");
                    inProc6ArtifactDirPath = Path.Combine(_inProc6ExtractedRootDir, $"{artifactName}.inproc6.{cliVersion}");
                }

                // Copy in-proc8 files
                var inProc8FinalDestination = Path.Combine(consolidatedArtifactDirPath, Constants.InProc8DirectoryName);
                FileUtilities.CopyDirectory(inProc8ArtifactDirPath, inProc8FinalDestination);
                Console.WriteLine($"Copied files from {inProc8ArtifactDirPath} => {inProc8FinalDestination}");

                // Copy in-proc6 files
                var inProc6FinalDestination = Path.Combine(consolidatedArtifactDirPath, Constants.InProc6DirectoryName);
                FileUtilities.CopyDirectory(inProc6ArtifactDirPath, inProc6FinalDestination);
                Console.WriteLine($"Copied files from {inProc6ArtifactDirPath} => {inProc6FinalDestination}");

                Console.WriteLine($"Finished assembling {consolidatedArtifactDirPath}\n");
            }

            Directory.Delete(_outOfProcExtractedRootDir, true);
            Console.WriteLine("Finished assembling CLI Core Tools artifacts\n");
        }

        private static (string ArtifactDirectory, string Version) GetArtifactDirectoryAndVersionNumber(string extractedRootDirectory, string artifactName)
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

        private void ExtractZipFilesInDirectory(string zipSourceDir, string extractDestinationDir)
        {
            if (!Directory.Exists(zipSourceDir))
            {
                Console.WriteLine($"Directory '{zipSourceDir}' does not exist.");
                return;
            }

            var zipFiles = Directory.GetFiles(zipSourceDir, "*.zip");

            if (!string.IsNullOrEmpty(_artifactName))
            {
                zipFiles = [.. zipFiles.Where(file => Path.GetFileName(file).StartsWith(_artifactName, StringComparison.OrdinalIgnoreCase))];
            }

            Console.WriteLine($"{zipFiles.Length} zip file(s) found in '{zipSourceDir}'.");

            foreach (var zipFile in zipFiles)
            {
                var fileName = Path.GetFileName(zipFile);
                var destinationDir = Path.Combine(extractDestinationDir, Path.GetFileNameWithoutExtension(zipFile));

                if (!string.IsNullOrEmpty(_artifactName))
                {
                    var destZipFile = Path.Combine(extractDestinationDir, fileName);
                    File.Copy(zipFile, destZipFile, overwrite: true);
                    Console.WriteLine($"Copied '{zipFile}' to '{destZipFile}'");
                }

                Console.WriteLine($"Extracting '{zipFile}' to '{destinationDir}'");
                FileUtilities.ExtractToDirectory(zipFile, destinationDir);
                File.Delete(zipFile);
            }
        }

        [GeneratedRegex(Constants.CoreToolsProductVersionPattern)]
        private static partial Regex CoreToolsVersionRegex();
    }
}
