// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Func.TestFramework.Helpers
{
    public static class CopyDirectoryHelpers
    {
        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Create all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
            }

            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string destFile = filePath.Replace(sourceDir, destinationDir);
                File.Copy(filePath, destFile, true);
            }
        }

        public static void CopyDirectoryWithout(string sourceDir, string destinationDir, string excludeFile)
        {
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
                    File.Copy(filePath, destFile, true);
                }
            }
        }
    }
}
