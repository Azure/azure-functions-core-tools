// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.TestFramework.Helpers
{
    public static class CopyDirectoryHelpers
    {
        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            if (!Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
            }

            // Create destination directory if it doesn't exist
            Directory.CreateDirectory(destinationDir);

            // Create all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
            }

            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string destFile = filePath.Replace(sourceDir, destinationDir);

                // Ensure the destination directory exists (for nested paths)
                string? destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(filePath, destFile, true);
            }
        }

        public static void CopyDirectoryWithout(string sourceDir, string destinationDir, string excludeFile)
        {
            if (!Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
            }

            // Create destination directory if it doesn't exist
            Directory.CreateDirectory(destinationDir);

            // Create all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
            }

            // Copy all files except the excluded one
            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (!Path.GetFileName(filePath).Equals(excludeFile, StringComparison.OrdinalIgnoreCase))
                {
                    string destFile = filePath.Replace(sourceDir, destinationDir);

                    // Ensure the destination directory exists (for nested paths)
                    string? destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Copy(filePath, destFile, true);
                }
            }
        }
    }
}
