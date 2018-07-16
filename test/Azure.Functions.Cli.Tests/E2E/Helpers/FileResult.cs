namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public class FileResult
    {
        public string Name { get; internal set; }
        public string[] ContentContains { get; internal set; }
        public string ContentIs { get; internal set; }
        public bool Exists { get; set; } = true;
    }
}