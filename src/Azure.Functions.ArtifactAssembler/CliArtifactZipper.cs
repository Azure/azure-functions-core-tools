using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.ArtifactAssembler
{
    internal class CliArtifactZipper
    {
        private readonly string _rootWorkingDirectory;
        public CliArtifactZipper(string rootWorkingDirectory)
        {
            _rootWorkingDirectory = rootWorkingDirectory;

        }
        internal void ZipCliArtifacts()
        {
            string stagingDirectory = Path.Combine(_rootWorkingDirectory, Constants.StagingDirName, Constants.CliOutputArtifactDirectoryName);

            // Get all directories in the staging directory
            var directories = Directory.GetDirectories(stagingDirectory);

            foreach (var dir in directories)
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
    }
}
