// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.Compression;

namespace Azure.Functions.ArtifactAssembler
{
    internal static class FileUtilities
    {
        /// <summary>
        /// Copies all files and subdirectories from the source directory to the destination directory.
        /// </summary>
        /// <param name="sourceDir">The source directory to copy from.</param>
        /// <param name="destinationDir">The destination directory. If this directory does not exist, it will be created.</param>
        internal static void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Create the destination directory if it doesn't exist
            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            // Copy all files from the source directory to the destination directory
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destinationFilePath = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destinationFilePath, true);
            }

            // Recursively copy all subdirectories
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destinationSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destinationSubDir);
            }
        }

        /// <summary>
        /// Creates a zip file from the specified source directory and saves it to the specified zip file path.
        /// </summary>
        internal static void CreateZipFile(string sourceDirectory, string zipPath)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                throw new Exception($"source directory '{sourceDirectory}' does not exist.");
            }

            ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }


        /// <summary>
        /// Extracts the specified zip file to the specified destination directory and returns the path to the extracted directory.
        /// </summary>
        /// <param name="zipFilePath">The path to the zip file to be extracted.</param>
        /// <param name="destinationDirectory">The destination directory to where the extraction happens.</param>
        /// <returns>The path to the directory where extracted content is present.</returns>
        internal static string ExtractToDirectory(string zipFilePath, string destinationDirectory)
        {
            ZipFile.ExtractToDirectory(zipFilePath, destinationDirectory);
            return Path.Combine(destinationDirectory, Path.GetFileNameWithoutExtension(zipFilePath));
        }
    }
}