using System;
using System.IO;
using System.IO.Compression;
using Azure.Functions.Cli.Helpers;
using Mono.Unix;

namespace Azure.Functions.Cli.Extensions
{
    public static class ZipArchiveExtensions
    {
        public static void AddFile(this ZipArchive archive, string fileName, string zippedName, string zipRoot)
        {
            using (var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                archive.AddFile(zippedName, zipRoot, stream);
            }
        }

        public static bool TryGetLinuxFileAttributes(string fileName, out int fileAttribs)
        {
            try
            {
                var unixFileInfo = new UnixFileInfo(fileName);
                fileAttribs = (int) unixFileInfo.FileAccessPermissions | (int) unixFileInfo.FileSpecialAttributes;
                fileAttribs = fileAttribs << 16;
                return true;
            }
            catch (Exception)
            {
                // If any error occur, we go back to not updating attributes
                // This is a safety net to make sure this temporary solution does not set people up for failure
                fileAttribs = 0;
                return false;
            }
        }

        public static void AddFile(this ZipArchive archive, string fileName, string zipRoot, Stream contentStream)
        {
            var entry = archive.CreateEntry(fileName.FixFileNameForZip(zipRoot), CompressionLevel.Fastest);

            // If not Windows, we can assume it's a Unix environment
            // For Unix environments, we need to explicity add file attributes in the Zipfile
            if (!PlatformHelper.IsWindows)
            {
                if (TryGetLinuxFileAttributes(fileName, out int fileAttributes))
                {
                    entry.ExternalAttributes = fileAttributes;
                }
            }

            using (var zipStream = entry.Open())
            {
                contentStream.CopyTo(zipStream);
            }
        }

        public static string FixFileNameForZip(this string value, string zipRoot)
        {
            return value.Substring(zipRoot.Length).TrimStart(new[] { '\\', '/' }).Replace('\\', '/');
        }
    }
}
