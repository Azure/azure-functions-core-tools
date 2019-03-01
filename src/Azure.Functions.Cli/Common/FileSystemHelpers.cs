using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Common
{
    internal static class FileSystemHelpers
    {
        private static IFileSystem _default = new FileSystem();
        private static IFileSystem _instance;

        public static IFileSystem Instance
        {
            get { return _instance ?? _default; }
            set { _instance = value; }
        }

        public static Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.None)
        {
            return Instance.File.Open(path, mode, access, share);
        }

        internal static byte[] ReadAllBytes(string path)
        {
            return Instance.File.ReadAllBytes(path);
        }

        public static string ReadAllTextFromFile(string path)
        {
            return Instance.File.ReadAllText(path);
        }

        public static void Copy(string source, string destination, bool overwrite = false)
        {
            Instance.File.Copy(source, destination, overwrite);
        }

        public static async Task<string> ReadAllTextFromFileAsync(string path)
        {
            using (var fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var streamReader = new StreamReader(fileStream))
            {
                return await streamReader.ReadToEndAsync();
            }
        }

        public static void WriteAllTextToFile(string path, string content)
        {
            Instance.File.WriteAllText(path, content);
        }

        public static async Task WriteAllTextToFileAsync(string path, string content)
        {
            using (var fileStream = OpenFile(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (var streamWriter = new StreamWriter(fileStream))
            {
                await streamWriter.WriteAsync(content);
                await streamWriter.FlushAsync();
            }
        }

        internal static void WriteAllBytes(string path, byte[] bytes)
        {
            Instance.File.WriteAllBytes(path, bytes);
        }

        public static async Task WriteToFile(string path, Stream stream)
        {
            using (var fileStream = OpenFile(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fileStream);
            }
        }

        public static bool FileExists(string path)
        {
            return Instance.File.Exists(path);
        }

        public static bool DirectoryExists(string path)
        {
            return Instance.Directory.Exists(path);
        }

        public static void FileDelete(string path)
        {
            Instance.File.Delete(path);
        }

        public static void CreateDirectory(string path)
        {
            Instance.Directory.CreateDirectory(path);
        }

        public static void CreateFile(string path)
        {
            Instance.File.Create(path);
        }

        public static string EnsureDirectory(string path)
        {
            if (!DirectoryExists(path))
            {
                CreateDirectory(path);
            }
            return path;
        }

        public static void DeleteDirectorySafe(string path, bool ignoreErrors = true)
        {
            DeleteFileSystemInfo(Instance.DirectoryInfo.FromDirectoryName(path), ignoreErrors);
        }

        public static IEnumerable<string> GetLocalFiles(string path, GitIgnoreParser ignoreParser = null, bool returnIgnored = false)
        {
            var ignoredDirectories = new[] { ".git", ".vscode" };
            var ignoredFiles = new[] { ".funcignore", ".gitignore", "appsettings.json", "local.settings.json", "project.lock.json" };

            foreach (var file in FileSystemHelpers.GetFiles(path, ignoredDirectories, ignoredFiles))
            {
                if (preCondition(file))
                {
                    yield return file;
                }
            }

            bool preCondition(string file)
            {
                var fileName = file.Replace(path, string.Empty).Trim(Path.DirectorySeparatorChar).Replace("\\", "/");
                return (returnIgnored ? ignoreParser?.Denies(fileName) : ignoreParser?.Accepts(fileName)) ?? true;
            }
        }

        internal static IEnumerable<string> GetFiles(string directoryPath, IEnumerable<string> excludedDirectories = null, IEnumerable<string> excludedFiles = null, string searchPattern = "*")
        {
            foreach (var file in Instance.Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (excludedFiles == null ||
                    !excludedFiles.Any(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return file;
                }
            }

            foreach (var directory in Instance.Directory.GetDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                var directoryName = Path.GetFileName(directory);
                if (excludedDirectories == null ||
                    !excludedDirectories.Any(d => d.Equals(directoryName, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var file in GetFiles(directory, excludedDirectories, excludedFiles, searchPattern))
                    {
                        yield return file;
                    }
                }
            }
        }

        internal static bool FileExists(object funcIgnoreFile)
        {
            throw new NotImplementedException();
        }

        internal static IEnumerable<string> GetDirectories(string path)
        {
            return Instance.Directory.GetDirectories(path);
        }

        private static void DeleteFileSystemInfo(FileSystemInfoBase fileSystemInfo, bool ignoreErrors)
        {
            if (!fileSystemInfo.Exists)
            {
                return;
            }

            try
            {
                fileSystemInfo.Attributes = FileAttributes.Normal;
            }
            catch
            {
                if (!ignoreErrors) throw;
            }

            var directoryInfo = fileSystemInfo as DirectoryInfoBase;

            if (directoryInfo != null)
            {
                DeleteDirectoryContentsSafe(directoryInfo, ignoreErrors);
            }

            DoSafeAction(fileSystemInfo.Delete, ignoreErrors);
        }

        private static void DeleteDirectoryContentsSafe(DirectoryInfoBase directoryInfo, bool ignoreErrors)
        {
            try
            {
                if (directoryInfo.Exists)
                {
                    foreach (var fsi in directoryInfo.GetFileSystemInfos())
                    {
                        DeleteFileSystemInfo(fsi, ignoreErrors);
                    }
                }
            }
            catch
            {
                if (!ignoreErrors) throw;
            }
        }

        private static void DoSafeAction(Action action, bool ignoreErrors)
        {
            try
            {
                action();
            }
            catch
            {
                if (!ignoreErrors) throw;
            }
        }
    }
}
