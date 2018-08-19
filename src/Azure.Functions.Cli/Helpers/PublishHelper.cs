using System.IO;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public class PublishHelper
    {
        public static GitIgnoreParser GetIgnoreParser(string workingDir)
        {
            try
            {
                var path = Path.Combine(workingDir, Constants.FuncIgnoreFile);
                if (FileSystemHelpers.FileExists(path))
                {
                    return new GitIgnoreParser(FileSystemHelpers.ReadAllTextFromFile(path));
                }
            }
            catch { }
            return null;
        }
    }
}