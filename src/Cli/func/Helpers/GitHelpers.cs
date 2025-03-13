using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public static class GitHelpers
    {
        public static string ActionsHashFiles(IEnumerable<string> files)
        {
            var digests = new MemoryStream();
            foreach (var file in files)
            {
                using (var sha = SHA256.Create())
                {
                    try
                    {
                        using (var stream = File.Open(file, FileMode.Open))
                        {
                            stream.Position = 0;
                            digests.Write(sha.ComputeHash(stream));
                        }
                    }
                    catch (IOException e)
                    {
                        Console.WriteLine($"I/O Exception: {e.Message}");
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Console.WriteLine($"Access Exception: {e.Message}");
                    }
                }
            }
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(digests.ToArray());
                var str = "";
                for (int i = 0; i < hash.Length; i++)
                {
                    str += $"{hash[i]:x2}";
                }
                return str;
            }
        }

        public static async Task<(string output, string error, int exit)> GitHash(bool ignoreError = false)
        {
            var git = new Executable("git", "describe --always --dirty");
            var sbError = new StringBuilder();
            var sbOutput = new StringBuilder();

            var exitCode = await git.RunAsync(l => sbOutput.AppendLine(l), e => sbError.AppendLine(e));

            if (exitCode != 0 && !ignoreError)
            {
                throw new CliException($"Error running {git.Command}.\n" +
                    $"output: {sbOutput.ToString()}\n{sbError.ToString()}");
            }

            return (trim(sbOutput.ToString()), trim(sbError.ToString()), exitCode);

            string trim(string str) => str.Trim(new[] { ' ', '\n' });
        }
    }
}