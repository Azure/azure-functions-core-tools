// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.Text;
using Colors.Net;

namespace Azure.Functions.Cli.Common
{
    internal static class FileSystemHelpers
    {
        private static readonly IFileSystem _default = new FileSystem();

        // Ambient, async-aware scope for the active IFileSystem
        private static readonly AsyncLocal<IFileSystem> _ambient = new();

        /// <summary>
        /// Gets the ambient filesystem if present, otherwise the process default.
        /// </summary>
        internal static IFileSystem Current => _ambient.Value ?? _default;

        /// <summary>
        /// Gets FileSystem "Instance".
        /// Setter is obsolete; use Override(...) in tests.
        /// </summary>
        public static IFileSystem Instance
        {
            get => Current;
        }

        /// <summary>
        /// For Tests Only:
        /// Temporarily overrides the ambient filesystem for the lifetime of the returned IDisposable.
        /// Safe for parallel tests via AsyncLocal.
        /// </summary>
        internal static IDisposable Override(IFileSystem fileSystem)
            => new FsOverride(fileSystem);

        // -----------------------------
        // Below is the existing surface
        // -----------------------------
        public static Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.None)
            => Current.File.Open(path, mode, access, share);

        internal static byte[] ReadAllBytes(string path)
            => Current.File.ReadAllBytes(path);

        public static string ReadAllTextFromFile(string path)
            => Current.File.ReadAllText(path);

        public static void Copy(string source, string destination, bool overwrite = false)
            => Current.File.Copy(source, destination, overwrite);

        public static async Task<string> ReadAllTextFromFileAsync(string path)
        {
            using var fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var streamReader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await streamReader.ReadToEndAsync().ConfigureAwait(false);
        }

        public static void WriteAllTextToFile(string path, string content)
            => Current.File.WriteAllText(path, content);

        public static async Task WriteAllTextToFileAsync(string path, string content)
        {
            using var fileStream = OpenFile(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var streamWriter = new StreamWriter(fileStream);
            await streamWriter.WriteAsync(content).ConfigureAwait(false);
            await streamWriter.FlushAsync().ConfigureAwait(false);
        }

        public static async Task WriteFileIfNotExists(string fileName, string fileContent)
        {
            if (!FileExists(fileName))
            {
                ColoredConsole.WriteLine($"Writing {fileName}");
                await WriteAllTextToFileAsync(fileName, fileContent).ConfigureAwait(false);
            }
            else
            {
                ColoredConsole.WriteLine($"{fileName} already exists. Skipped!");
            }
        }

        internal static void WriteAllBytes(string path, byte[] bytes)
            => Current.File.WriteAllBytes(path, bytes);

        public static async Task WriteToFile(string path, Stream stream)
        {
            using var fileStream = OpenFile(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream).ConfigureAwait(false);
        }

        public static bool FileExists(string path)
            => Current.File.Exists(path);

        public static bool DirectoryExists(string path)
            => Current.Directory.Exists(path);

        public static void FileDelete(string path)
            => Current.File.Delete(path);

        // Removed accidental duplicate NotImplementedException overload
        public static void CreateDirectory(string path)
            => Current.Directory.CreateDirectory(path);

        public static void CreateFile(string path)
        {
            // Ensure the handle is disposed; FileSystem's Create returns a stream.
#pragma warning disable SA1312 // Variable names should begin with lower-case letter
            using var _ = Current.File.Create(path);
#pragma warning restore SA1312 // Variable names should begin with lower-case letter
        }

        public static string EnsureDirectory(string path)
        {
            if (!DirectoryExists(path))
            {
                CreateDirectory(path);
            }

            return path;
        }

        public static bool EnsureDirectoryNotEmpty(string path)
            => DirectoryExists(path) && Current.Directory.EnumerateFileSystemEntries(path).Any();

        public static void DeleteDirectorySafe(string path, bool ignoreErrors = true)
            => DeleteFileSystemInfo(Current.DirectoryInfo.FromDirectoryName(path), ignoreErrors);

        public static IEnumerable<string> GetLocalFiles(string path, GitIgnoreParser ignoreParser = null, bool returnIgnored = false, IEnumerable<string> additionalIgnoredDirectories = null)
        {
            List<string> ignoredDirectories = new() { ".git", ".vscode" };
            if (additionalIgnoredDirectories is not null)
            {
                ignoredDirectories.AddRange(additionalIgnoredDirectories);
            }

            var ignoredFiles = new[] { ".funcignore", ".gitignore", "local.settings.json", "project.lock.json" };

            foreach (var file in GetFiles(path, ignoredDirectories, ignoredFiles))
            {
                var fileName = file.Replace(path, string.Empty).Trim(Path.DirectorySeparatorChar).Replace("\\", "/");
                bool pass = (returnIgnored ? ignoreParser?.Denies(fileName) : ignoreParser?.Accepts(fileName)) ?? true;
                if (pass)
                {
                    yield return file;
                }
            }
        }

        internal static IEnumerable<string> GetFiles(string directoryPath, IEnumerable<string> excludedDirectories = null, IEnumerable<string> excludedFiles = null, string searchPattern = "*", SearchOption searchOption = SearchOption.AllDirectories)
        {
            foreach (var file in Current.Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (excludedFiles is null || !excludedFiles.Any(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return file;
                }
            }

            if (searchOption == SearchOption.TopDirectoryOnly)
            {
                yield break;
            }

            foreach (var directory in Current.Directory.GetDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                var directoryName = Path.GetFileName(directory);
                if (excludedDirectories is null || !excludedDirectories.Any(d => d.Equals(directoryName, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var file in GetFiles(directory, excludedDirectories, excludedFiles, searchPattern, searchOption))
                    {
                        yield return file;
                    }
                }
            }
        }

        internal static IEnumerable<string> GetDirectories(string path)
            => Current.Directory.GetDirectories(path);

        private static void DeleteFileSystemInfo(FileSystemInfoBase fileSystemInfo, bool ignoreErrors)
        {
            if (!fileSystemInfo.Exists)
            {
                return;
            }

            Try(ignoreErrors, () => fileSystemInfo.Attributes = FileAttributes.Normal);

            if (fileSystemInfo is DirectoryInfoBase dir)
            {
                DeleteDirectoryContentsSafe(dir, ignoreErrors);
            }

            Try(ignoreErrors, fileSystemInfo.Delete);
        }

        private static void DeleteDirectoryContentsSafe(DirectoryInfoBase directoryInfo, bool ignoreErrors)
        {
            Try(ignoreErrors, () =>
            {
                if (!directoryInfo.Exists)
                {
                    return;
                }

                foreach (var fsi in directoryInfo.GetFileSystemInfos())
                {
                    DeleteFileSystemInfo(fsi, ignoreErrors);
                }
            });
        }

        private static void Try(bool ignoreErrors, Action action)
        {
            try
            {
                action();
            }
            catch when (ignoreErrors)
            {
            }
        }

        private sealed class FsOverride : IDisposable
        {
            private readonly IFileSystem _previous;
            private bool _disposed;

            public FsOverride(IFileSystem next)
            {
                _previous = _ambient.Value;
                _ambient.Value = next ?? throw new ArgumentNullException(nameof(next));
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _ambient.Value = _previous;
            }
        }
    }
}
