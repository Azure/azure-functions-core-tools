// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
            _stagingDirectory = CreateStagingDirectory(_rootWorkingDirectory);
            _artifactName = artifactName;
        }

        internal void Assemble()
        {
            ExtractDownloadedArtifacts();
            CreateCliCoreTools();
            CreateVisualStudioCoreTools();
        }

        private static string GetRequiredEnvironmentVariable(string variableName)
        {
            return Environment.GetEnvironmentVariable(variableName)
                ?? throw new InvalidDataException($"The `{variableName}` environment variable value is missing!");
        }

        private void ExtractDownloadedArtifacts()
        {
            var inProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _inProcArtifactDirectoryName);
            var coreToolsHostArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _coreToolsHostArtifactDirectoryName);
            var outOfProcArtifactDownloadDir = Path.Combine(_rootWorkingDirectory, _outOfProcArtifactDirectoryName);

            var inProc6ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc6ArtifactName);
            var inProc8ArtifactDirPath = Path.Combine(inProcArtifactDownloadDir, _inProc8ArtifactName);
            var outOfProcArtifactDirPath = Path.Combine(outOfProcArtifactDownloadDir, _outOfProcArtifactName);

            var coreToolsHostWindowsArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostWindowsArtifactName);
            var coreToolsHostLinuxArtifactDirPath = Path.Combine(coreToolsHostArtifactDownloadDir, _coreToolsHostLinuxArtifactName);

            CheckIfArtifactDirectoryExists(inProc6ArtifactDirPath);
            CheckIfArtifactDirectoryExists(inProc8ArtifactDirPath);
            CheckIfArtifactDirectoryExists(outOfProcArtifactDirPath);
            CheckIfArtifactDirectoryExists(coreToolsHostWindowsArtifactDirPath);
            CheckIfArtifactDirectoryExists(coreToolsHostLinuxArtifactDirPath);

            _inProc6ExtractedRootDir = PrepareStagingDirectory(inProc6ArtifactDirPath, Path.Combine(_stagingDirectory, Constants.InProc6DirectoryName));
            _inProc8ExtractedRootDir = PrepareStagingDirectory(inProc8ArtifactDirPath, Path.Combine(_stagingDirectory, Constants.InProc8DirectoryName));

            Directory.Delete(inProcArtifactDownloadDir, true);

            _coreToolsHostExtractedRootDir = PrepareStagingDirectory(coreToolsHostWindowsArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName), true);
            PrepareStagingDirectory(coreToolsHostLinuxArtifactDirPath, Path.Combine(_stagingDirectory, Constants.CoreToolsHostDirectoryName), true);
            Directory.Delete(coreToolsHostArtifactDownloadDir, true);

            _outOfProcExtractedRootDir = PrepareStagingDirectory(outOfProcArtifactDirPath, Path.Combine(_stagingDirectory, Constants.OutOfProcDirectoryName));
            Directory.Delete(outOfProcArtifactDownloadDir, true);
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
            var customHostTargetArtifactDir = Path.Combine(_stagingDirectory, Constants.VisualStudioOutputArtifactDirectoryName);
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
                var inProc6ArtifactDirPath = Path.Combine(_inProc6ExtractedRootDir, artifactDirName);
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
            Directory.Delete(_coreToolsHostExtractedRootDir, true);

            Console.WriteLine("Finished assembling Visual Studio Core Tools artifacts");
        }

        // This method creates a new directory for the core tools host and copies the inproc8 files
        private (string artifactDirName, string consolidatedArtifactDirPath) CreateInProc8CoreToolsHostHelper(
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
            var cliCoreToolsTargetArtifactDir = Path.Combine(_stagingDirectory, Constants.CliOutputArtifactDirectoryName);
            Directory.CreateDirectory(cliCoreToolsTargetArtifactDir);

            string outOfProcVersion = string.Empty,
                   inProcVersion = string.Empty,
                   outOfProcArtifactDirPath = string.Empty,
                   inProc8ArtifactDirPath = string.Empty;

            string[] cliArtifactList = string.IsNullOrEmpty(_artifactName) ? _cliArtifacts : [_artifactName];

            foreach (var artifactName in cliArtifactList)
            {
                // Set up out-of-proc artifact directory and version
                if (string.IsNullOrEmpty(outOfProcArtifactDirPath))
                {
                    var (artifactDirectory, version) = GetArtifactDirectoryAndVersionNumber(_outOfProcExtractedRootDir, artifactName);
                    outOfProcArtifactDirPath = artifactDirectory;
                    outOfProcVersion = version;
                }
                else
                {
                    var artifactNameWithVersion = $"{artifactName}.{outOfProcVersion}";
                    outOfProcArtifactDirPath = Path.Combine(_outOfProcExtractedRootDir, artifactNameWithVersion);
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

                // If we are running this for the first time, extract the directory path and out of proc version
                if (string.IsNullOrEmpty(inProc8ArtifactDirPath))
                {
                    // Get the version number from the in-proc build since it will be different than out-of-proc
                    var (artifactDirectory, version) = GetArtifactDirectoryAndVersionNumber(_inProc8ExtractedRootDir, artifactName);
                    inProc8ArtifactDirPath = artifactDirectory;
                    inProcVersion = version;
                }
                else
                {
                    var artifactNameWithVersion = $"{artifactName}.{inProcVersion}";
                    inProc8ArtifactDirPath = Path.Combine(_inProc8ExtractedRootDir, artifactNameWithVersion);
                }

                // Copy in-proc8 files
                var inProc8FinalDestination = Path.Combine(consolidatedArtifactDirPath, Constants.InProc8DirectoryName);
                FileUtilities.CopyDirectory(inProc8ArtifactDirPath, inProc8FinalDestination);
                Console.WriteLine($"Copied files from {inProc8ArtifactDirPath} => {inProc8FinalDestination}");

                // Rename inproc6 directory to have the same version as the out-of-proc artifact before copying
                var inProcArtifactName = Path.GetFileName(inProc8ArtifactDirPath);
                var inProc6ArtifactDirPath = Path.Combine(_inProc6ExtractedRootDir, inProcArtifactName);
                CheckIfArtifactDirectoryExists(inProc6ArtifactDirPath);

                // Copy in-proc6 files
                var inProc6FinalDestination = Path.Combine(consolidatedArtifactDirPath, Constants.InProc6DirectoryName);
                FileUtilities.CopyDirectory(inProc6ArtifactDirPath, inProc6FinalDestination);
                Console.WriteLine($"Copied files from {inProc6ArtifactDirPath} => {inProc6FinalDestination}");

                Console.WriteLine($"Finished assembling {consolidatedArtifactDirPath}\n");
            }

            Directory.Delete(_outOfProcExtractedRootDir, true);
            Console.WriteLine("Finished assembling CLI Core Tools artifacts\n");
        }

        private static (string artifactDirectory, string version) GetArtifactDirectoryAndVersionNumber(string extractedRootDirectory, string artifactName)
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