using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Build
{
    public static class FileHelpers
    {

        public static void EnsureDirectoryExists(string dirPath)
        {
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }
        }

        // https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
        public static void RecursiveCopy(string sourcePath, string destinationPath)
        {
            // Get the subdirectories for the specified directory.
            var dir = new DirectoryInfo(sourcePath);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourcePath);
            }

            var dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            // Get the files in the directory and copy them to the new location.
            var files = dir.GetFiles();
            foreach (var file in files)
            {
                var temppath = Path.Combine(destinationPath, file.Name);
                file.CopyTo(temppath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            foreach (var subdir in dirs)
            {
                var temppath = Path.Combine(destinationPath, subdir.Name);
                RecursiveCopy(subdir.FullName, temppath);
            }
        }

        public static IEnumerable<string> ExpandFileWildCardEntries(IEnumerable<string> filesAndDirs)
        {
            var allEntries = new List<string>();
            foreach (var entry in filesAndDirs)
            {
                if (entry.Contains("*"))
                {
                    var files = Directory.GetFiles(Path.GetDirectoryName(entry), Path.GetFileName(entry), SearchOption.AllDirectories);
                    allEntries.AddRange(files);
                }
                else
                {
                    allEntries.Add(entry);
                }
            }
            return allEntries;
        }

        public static IEnumerable<string> GetAllFilesFromFilesAndDirs(IEnumerable<string> filesAndDirs)
        {
            var allFiles = new List<string>();
            foreach (var entry in filesAndDirs)
            {
                // Just in case if the file we need to sign does not exist anymore or in this build
                if (!Directory.Exists(entry) && !File.Exists(entry))
                {
                    continue;
                }
                FileAttributes attr = File.GetAttributes(entry);
                if (attr.HasFlag(FileAttributes.Directory))
                {
                    var files = Directory.GetFiles(entry, "*", SearchOption.AllDirectories);
                    allFiles.AddRange(files);
                }
                else
                {
                    allFiles.Add(entry);
                }
            }
            return allFiles;
        }

        public static void CopyFileRelativeToBase(string sourceFilePath, string targetDirectory, string baseDirectory)
        {
            string relativePath = Path.GetRelativePath(baseDirectory, sourceFilePath);
            string fullFilePath = Path.Combine(targetDirectory, relativePath);
            string directoryPath = Path.GetDirectoryName(fullFilePath);
            Directory.CreateDirectory(directoryPath);
            File.Copy(sourceFilePath, fullFilePath);
        }

        public static void CreateZipFile(IEnumerable<string> files, string baseDir, string zipFilePath)
        {
            using (var zipfile = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    zipfile.CreateEntryFromFile(file, Path.GetRelativePath(baseDir, file));
                }
            }
        }

        public static void ExtractZipFileForce(string zipFile, string to)
        {
            using (var archive = ZipFile.OpenRead(zipFile))
            {
                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    file.ExtractToFile(Path.Combine(to, file.FullName), overwrite: true);
                }
            }
        }

        public static void ExtractZipToDirectory(string zipFile, string to)
        {
            ZipFile.ExtractToDirectory(zipFile, to);
        }
    }
}