using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Helpers
{
    public static class ZipHelper
    {
        public static async Task<Stream> GetAppZipFile(string functionAppRoot, bool buildNativeDeps, BuildOption buildOption, bool noBuild, GitIgnoreParser ignoreParser = null, string additionalPackages = null, bool ignoreDotNetCheck = false)
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

            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.python && !noBuild)
            {
                return await PythonHelpers.GetPythonDeploymentPackage(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot, buildNativeDeps, buildOption, additionalPackages);
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet && !ignoreDotNetCheck && !noBuild && buildOption != BuildOption.Remote)
            {
                throw new CliException("Pack command doesn't work for dotnet functions");
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet && buildOption == BuildOption.Remote)
            {
                // temporarily provide an escape hatch to use gozip in case there are bugs in the dotnet implementation
                bool useGoZip = EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.UseGoZip);

                // Remote build for dotnet does not require bin and obj folders. They will be generated during the oryx build
                return await CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false, new string[] { "bin", "obj" }), functionAppRoot, Enumerable.Empty<string>(), useGoZip);
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

        public static async Task<Stream> CreateZip(IEnumerable<string> files, string rootPath, IEnumerable<string> executables, bool useGoZip = false)
        {
            if (useGoZip)
            {
                if (GoZipExists(out string goZipLocation))
                {
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
            int UnixExecutablePermissions = Convert.ToInt32("100777", 8) << 16;
            int UnixReadWritePermissions = Convert.ToInt32("100666", 8) << 16;

            var memStream = new MemoryStream();

            using (var zip = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    var entryName = file.FixFileNameForZip(rootPath);
                    var entry = zip.CreateEntryFromFile(file, entryName);

                    if (executables.Contains(entryName))
                    {
                        entry.ExternalAttributes = UnixExecutablePermissions; // Mark as executable in Unix
                    }
                    else
                    {
                        entry.ExternalAttributes = UnixReadWritePermissions; // Mark as read/write in Unix
                    }
                }
            }

            // In order to properly mount and/or unzip this in Azure, we need to create the zip as if it were
            // Unix so that the correct file permissions set above are applied. To do this, we walk through the stream
            // and update the "created by" field to 3, which indicates it was created by Unix.
            if (OperatingSystem.IsWindows())
            {
                memStream.Seek(0, SeekOrigin.End);

                // Update the file header in the zip file for every file to indicate that it was created by Unix
                while (SeekBackwardsToSignature(memStream, centralDirectorySignature))
                {
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
            byte[] buffer = new byte[32];
            bool signatureFound = false;

            while (!signatureFound)
            {
                bufferPointer = SeekBackwardsAndRead(stream, buffer);

                if (bufferPointer == -1)
                {
                    break;
                }

                while (bufferPointer >= 0 && !signatureFound)
                {
                    currentSignature = (currentSignature << 8) | ((uint)buffer[bufferPointer]);
                    if (currentSignature == signatureToFind)
                    {
                        signatureFound = true;
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
                stream.Seek(bufferPointer, SeekOrigin.Current);
                return true;
            }
        }

        // Returns true if we are out of bytes
        private static int SeekBackwardsAndRead(Stream stream, byte[] buffer)
        {
            if (stream.Position >= buffer.Length)
            {
                stream.Seek(-buffer.Length, SeekOrigin.Current);
                stream.Read(buffer.AsSpan());
                stream.Seek(-buffer.Length, SeekOrigin.Current);
                return buffer.Length - 1;
            }

            // the thing we're looking for can't possibly fit; we're done
            return -1;
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