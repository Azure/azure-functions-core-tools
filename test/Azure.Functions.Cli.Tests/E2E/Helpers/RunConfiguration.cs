using System;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public class RunConfiguration
    {
        public string[] Commands { get; set; } = Array.Empty<string>();
        public bool ExpectExit { get; set; } = true;
        public bool ExitInError { get; set; } = false;
        public bool HasStandardError { get; set; } = false;
        public FileResult[] CheckFiles { get; set; } = Array.Empty<FileResult>();
        public DirectoryResult[] CheckDirectories { get; set; } = Array.Empty<DirectoryResult>();
        public string[] OutputContains { get; set; } = Array.Empty<string>();
        public string[] ErrorContains { get; set; } = Array.Empty<string>();
        public string[] OutputDoesntContain { get; set; } = Array.Empty<string>();
        public string[] ErrorDoesntContain { get; set; } = Array.Empty<string>();
        public Func<Task> Test { get; set; }
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public string CommandsStr => $"{string.Join(", ", Commands)}";
    }
}