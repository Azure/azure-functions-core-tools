// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Reflection;
using System.Text;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Helpers
{
    public static class ZipHelper
    {
        public static async Task<Stream> GetAppZipFile(string functionAppRoot, bool buildNativeDeps, BuildOption buildOption, bool noBuild, GitIgnoreParser ignoreParser = null, string additionalPackages = null)
        {
            var gitIgnorePath = Path.Combine(functionAppRoot, Constants.FuncIgnoreFile);
            if (ignoreParser == null && FileSystemHelpers.FileExists(gitIgnorePath))
            {
                ignoreParser = new GitIgnoreParser(await FileSystemHelpers.ReadAllTextFromFileAsync(gitIgnorePath));
            }

            if (noBuild)
            {
                ColoredConsole.WriteLine(DarkYellow("Skipping build event for functions project (--no-build)."));
            }
            else if (buildOption == BuildOption.Remote)
            {
                ColoredConsole.WriteLine(DarkYellow("Performing remote build for functions project."));
            }
            else if (buildOption == BuildOption.Local)
            {
                ColoredConsole.WriteLine(DarkYellow("Performing local build for functions project."));
            }

            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.Python && !noBuild)
            {
                return await PythonHelpers.GetPythonDeploymentPackage(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot, buildNativeDeps, buildOption, additionalPackages);
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.Dotnet && buildOption == BuildOption.Remote)
            {
                // Remote build for dotnet does not require bin and obj folders. They will be generated during the oryx build
                return await CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false, new string[] { "bin", "obj" }), functionAppRoot, Array.Empty<string>());
            }
            else
            {
                var customHandler = await HostHelpers.GetCustomHandlerExecutable();
                IEnumerable<string> executables = !string.IsNullOrEmpty(customHandler)
                    ? new[] { customHandler }
                    : Enumerable.Empty<string>();

                return await CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false), functionAppRoot, executables);
            }
        }

        public static async Task<Stream> CreateZip(IEnumerable<string> files, string rootPath, IEnumerable<string> executables)
        {
            // temporarily provide an escape hatch to use gozip in case there are bugs in the dotnet implementation
            bool useGoZip = EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.UseGoZip);

            if (useGoZip)
            {
                if (GoZipExists(out string goZipLocation))
                {
                    ColoredConsole.WriteLine(DarkYellow("Using gozip for packaging."));
                    var zipFilePath = Path.GetTempFileName();
                    return await CreateGoZip(files, rootPath, zipFilePath, goZipLocation, executables);
                }

                ColoredConsole.WriteLine(Yellow("Could not find gozip for packaging. Using DotNetZip to package. " +
                    "This may cause problems preserving file permissions when using in a Linux based environment."));
            }

            return CreateDotNetZip(files, rootPath, executables);
        }

        public static bool GoZipExists(out string fileLocation)
        {
            // It can be gozip.exe or gozip
            fileLocation = Directory.GetFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .Where(f => Path.GetFileNameWithoutExtension(f).Equals(Constants.GoZipFileName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (fileLocation != null)
            {
                return true;
            }

            return false;
        }

        public static Stream CreateDotNetZip(IEnumerable<string> files, string rootPath, IEnumerable<string> executables)
        {
            // See section 4.4.2.2 in the zip spec: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
            const byte CreatedByUnix = 3;

            // Signature that defines beginning of each file metadata. See section 4.3.12 in the zip spec above.
            const uint centralDirectorySignature = 0x02014B50;

            // Unix file permissions
            int unixExecutablePermissions = Convert.ToInt32("100777", 8) << 16;
            int unixReadWritePermissions = Convert.ToInt32("100666", 8) << 16;

            var memStream = new MemoryStream();

            using (var zip = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    var entryName = file.FixFileNameForZip(rootPath);
                    var entry = zip.CreateEntryFromFile(file, entryName);

                    entry.ExternalAttributes = executables.Contains(entryName) ? unixExecutablePermissions : unixReadWritePermissions;
                }
            }

            // In order to properly mount and/or unzip this in Azure, we need to create the zip as if it were
            // Unix so that the correct file permissions set above are applied. To do this, we walk backwards
            // through the stream and update the "created by" field to 3, which indicates it was created by Unix.
            if (OperatingSystem.IsWindows())
            {
                memStream.Seek(0, SeekOrigin.End);

                // Update the file header in the zip file for every file to indicate that it was created by Unix
                while (SeekBackwardsToSignature(memStream, centralDirectorySignature))
                {
                    // The field we need to set is 5 bytes from the beginning of the signature. Set it,
                    // then move back to the previous location so we can continue.
                    memStream.Seek(5, SeekOrigin.Current);
                    memStream.WriteByte(CreatedByUnix);
                    memStream.Seek(-6, SeekOrigin.Current);
                }
            }

            memStream.Seek(0, SeekOrigin.Begin);
            return memStream;
        }

        // Assumes all bytes of signatureToFind are non zero.
        // If the signature is found then returns true and positions stream at first byte of signature
        // If the signature is not found, returns false
        private static bool SeekBackwardsToSignature(Stream stream, uint signatureToFind)
        {
            int bufferPointer = 0;
            uint currentSignature = 0;

            // 32-byte buffer is arbitrary and is following the runtime implementation here:
            // https://github.com/dotnet/runtime/blob/ea97babd7ccfd2f6e9553093d315f26b51e4c7ac/src/libraries/System.IO.Compression/src/System/IO/Compression/ZipHelper.cs#L16
            byte[] buffer = new byte[32];

            bool outOfBytes = false;
            bool signatureFound = false;

            while (!signatureFound && !outOfBytes)
            {
                outOfBytes = SeekBackwardsAndRead(stream, buffer, out bufferPointer);

                while (bufferPointer >= 0 && !signatureFound)
                {
                    currentSignature = (currentSignature << 8) | ((uint)buffer[bufferPointer]);
                    if (currentSignature == signatureToFind)
                    {
                        signatureFound = true;
                        break;
                    }
                    else
                    {
                        bufferPointer--;
                    }
                }
            }

            if (!signatureFound)
            {
                return false;
            }
            else
            {
                // set the stream up to continue from here next call
                stream.Seek(bufferPointer, SeekOrigin.Current);
                return true;
            }
        }

        // Returns true if we are out of bytes
        // This method (and SeekBackwardsToSignature) are mostly copied from the runtime implementation here:
        // https://github.com/dotnet/runtime/blob/ea97babd7ccfd2f6e9553093d315f26b51e4c7ac/src/libraries/System.IO.Compression/src/System/IO/Compression/ZipHelper.cs#L172-L191
        private static bool SeekBackwardsAndRead(Stream stream, byte[] buffer, out int bufferPointer)
        {
            if (stream.Position >= buffer.Length)
            {
                stream.Seek(-buffer.Length, SeekOrigin.Current);
                stream.ReadExactly(buffer.AsSpan());
                stream.Seek(-buffer.Length, SeekOrigin.Current);
                bufferPointer = buffer.Length - 1;
                return false;
            }
            else
            {
                // if we cannot fill the buffer, read everything that's left and
                // return back that position in the buffer to the caller
                int bytesToRead = (int)stream.Position;
                stream.Seek(0, SeekOrigin.Begin);
                stream.ReadExactly(buffer, 0, bytesToRead);
                stream.Seek(0, SeekOrigin.Begin);
                bufferPointer = bytesToRead - 1;
                return true;
            }
        }

        public static async Task<Stream> CreateGoZip(IEnumerable<string> files, string rootPath, string zipFilePath, string goZipLocation, IEnumerable<string> executables)
        {
            var contentsFile = Path.GetTempFileName();
            await File.WriteAllLinesAsync(contentsFile, files);
            var args = new StringBuilder($"-base-dir \"{rootPath}\" -input-file \"{contentsFile}\" -output \"{zipFilePath}\"");
            foreach (var executable in executables)
            {
                args.Append($" --set-executable \"{executable}\"");
            }

            var goZipExe = new Executable(goZipLocation, args.ToString());
            await goZipExe.RunAsync();
            return new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        }

        public static string FixFileNameForZip(this string value, string zipRoot)
        {
            return value.Substring(zipRoot.Length).TrimStart(new[] { '\\', '/' }).Replace('\\', '/');
        }
    }
}
