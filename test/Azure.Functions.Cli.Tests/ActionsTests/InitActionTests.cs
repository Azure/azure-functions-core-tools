using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class InitActionTests : ActionTestsBase
    {
        public InitActionTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void InitActionTest()
        {
            // Test
            Program.Main(new[] { "init" });
            var files = Directory.GetFiles(WorkingDirectory).Select(Path.GetFileName);
            var folders = Directory.GetDirectories(WorkingDirectory).Select(Path.GetFileName);

            // Assert
            files.Should().HaveCount(3);
            files.Should().Contain(".gitignore");
            files.Should().Contain("host.json");
            files.Should().Contain("appsettings.json");

            var expectedFolders = 2;
            folders
                .Should()
                .HaveCount(expectedFolders,
                $"Expected to have {expectedFolders}, but got {folders.Count()}, which are {folders.Aggregate(string.Empty, (a, b) => string.Join(",", a, b))}");
            folders.Should().Contain(".git");
        }
    }
}
