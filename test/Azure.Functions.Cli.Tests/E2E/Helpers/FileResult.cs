using System;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public class FileResult
    {
        public string Name { get; set; }
        public string[] ContentContains { get; set; } = Array.Empty<string>();
        public string[] ContentNotContains { get; set; } = Array.Empty<string>();
        public string ContentIs { get; set; }
        public bool Exists { get; set; } = true;
    }
}