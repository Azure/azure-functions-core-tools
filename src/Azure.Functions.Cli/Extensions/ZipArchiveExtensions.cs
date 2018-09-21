using System.IO;
using System.IO.Compression;

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

        public static void AddFile(this ZipArchive archive, string fileName, string zipRoot, Stream contentStream)
        {
            var entry = archive.CreateEntry(fileName.FixFileNameForZip(zipRoot), CompressionLevel.Fastest);
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
