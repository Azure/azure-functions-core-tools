namespace Azure.Functions.Cli.Tests.E2ETests.Helpers
{
    public class DirectoryResult
    {
        public string Name { get; internal set; }
        public bool Exists { get; set; } = true;
    }
}