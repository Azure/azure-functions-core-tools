using System;
using System.IO;
using Azure.Functions.Cli.Helpers;
using Ionic.Zip;
using Mono.Unix;

namespace Azure.Functions.Cli.Extensions
{
    public static class ZipFileExtensions
    {
        public static void AddFile(this ZipFile file, string fileName, string zippedName, string zipRoot)
        {
            Stream openDelegate(string filename) => File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            void closeDelegate(string filename, Stream stream) => stream.Close();

            file.AddFile(zippedName, zipRoot, openDelegate, closeDelegate);
        }

        public static void AddFile(this ZipFile file, string fileName, string zipRoot, OpenDelegate openDelegate, CloseDelegate closeDelegate)
        {
            var entry = file.AddEntry(fileName.FixFileNameForZip(zipRoot), openDelegate, closeDelegate);

            // If not Windows, we can assume it's a Unix environment
            // For Unix environments, we need to explicity add file attributes in the Zipfile
            if (!PlatformHelper.IsWindows)
            {
                if (TryGetUnixFileAttributes(fileName, out int fileAttributes))
                {
                    // We have to use reflection to amend the private field in order to succefully
                    // propogate Unix file permissions.
                    // https://github.com/haf/DotNetZip.Semverd/blob/590a6d89ee7e4d79b24abf7deb928cf9456f08d9/src/Zip.Shared/ZipEntry.cs#L749
                    // https://stackoverflow.com/questions/12993962/set-value-of-private-field/12994027#12994027
                    var externalAttribProp = entry.GetType().GetField("_ExternalFileAttrs", System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance);

                    externalAttribProp.SetValue(entry, fileAttributes);
                }
            }
        }

        public static string FixFileNameForZip(this string value, string zipRoot)
        {
            return value.Substring(zipRoot.Length).TrimStart(new[] { '\\', '/' }).Replace('\\', '/');
        }

        private static bool TryGetUnixFileAttributes(string fileName, out int fileAttribs)
        {
            try
            {
                var unixFileInfo = new UnixFileInfo(fileName);
                fileAttribs = (int)unixFileInfo.FileAccessPermissions | (int)unixFileInfo.FileSpecialAttributes;
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
    }
}