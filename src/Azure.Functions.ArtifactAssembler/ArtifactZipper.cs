// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.Compression;

namespace Azure.Functions.ArtifactAssembler
{
    internal sealed class ArtifactZipper
    {
        private readonly string _rootWorkingDirectory;
        public ArtifactZipper(string rootWorkingDirectory)
        {
            _rootWorkingDirectory = rootWorkingDirectory;
        }

        internal void ZipCliArtifacts()
        {
            Console.WriteLine("Zipping CLI Artifacts");
            string stagingDirectory = Path.Combine(_rootWorkingDirectory, Constants.StagingDirName, Constants.CliOutputArtifactDirectoryName);

            // Get all directories in the staging directory
            var directories = Directory.EnumerateDirectories(stagingDirectory);

            foreach (var dir in directories)
            {
                // Create worker directory if it doesn't already exist
                CreateWorkerDirectoryIfDoesNotExist(dir);

                // Define zip file path and name
                string zipFileName = $"{new DirectoryInfo(dir).Name}.zip";
                string zipFilePath = Path.Combine(stagingDirectory, zipFileName);

                // Compress directory into zip file
                ZipFile.CreateFromDirectory(dir, zipFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
                Console.WriteLine($"Zipped: {dir} -> {zipFilePath}");

                // Verify zip creation and delete original directory to free up space
                if (File.Exists(zipFilePath))
                {
                    Console.WriteLine($"Successfully created zip: {zipFilePath}");
                    Directory.Delete(dir, true);
                    Console.WriteLine($"Deleted original directory: {dir}");
                }
                else
                {
                    Console.WriteLine($"Failed to create zip for: {dir}");
                }
            }

            Console.WriteLine("All directories zipped successfully!");
        }

        internal void ZipVisualStudioArtifacts()
        {
            Console.WriteLine("Zipping Visual Studio Artifacts");
            string stagingDirectory = Path.Combine(_rootWorkingDirectory, Constants.StagingDirName, Constants.VisualStudioOutputArtifactDirectoryName);

            // Get all directories in the staging directory
            var directories = Directory.EnumerateDirectories(stagingDirectory);

            foreach (var dir in directories)
            {
                // Define zip file path and name
                string zipFileName = $"{new DirectoryInfo(dir).Name}.zip";
                string zipFilePath = Path.Combine(stagingDirectory, zipFileName);

                // Compress directory into zip file
                FileUtilities.CreateZipFile(dir, zipFilePath);
                Console.WriteLine($"Zipped: {dir} -> {zipFilePath}");

                // Verify zip creation and delete original directory to free up space
                if (File.Exists(zipFilePath))
                {
                    Console.WriteLine($"Successfully created zip: {zipFilePath}");
                    Directory.Delete(dir, true);
                    Console.WriteLine($"Deleted original directory: {dir}");
                }
                else
                {
                    Console.WriteLine($"Failed to create zip for: {dir}");
                }
            }

            Console.WriteLine("All directories zipped successfully!");
        }

        internal void CreateWorkerDirectoryIfDoesNotExist(string dir)
        {
            string workersPath = Path.Combine(dir, "workers");

            // Ensure 'workers' directory exists
            if (!Directory.Exists(workersPath))
            {
                Directory.CreateDirectory(workersPath);
                Console.WriteLine($"Created missing 'workers' directory in {dir}");
            }
            else
            {
                Console.WriteLine($"'workers' directory already exists in {dir}");
            }

            // Add placeholder file if 'workers' directory is empty
            if (Directory.GetFiles(workersPath).Length == 0 && Directory.GetDirectories(workersPath).Length == 0)
            {
                File.WriteAllText(Path.Combine(workersPath, "placeholder.txt"), "Placeholder file");
                Console.WriteLine($"Created placeholder file in empty 'workers' directory in {dir}");
            }
            else
            {
                Console.WriteLine($"'workers' directory is not empty in {dir}");
            }
        }
    }
}
