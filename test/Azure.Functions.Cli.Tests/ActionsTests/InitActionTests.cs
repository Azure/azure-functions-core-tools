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

        //[Theory]
        [InlineData(null)]
        [InlineData(".")]
        [InlineData("newFolder")]
        [InlineData("..\\anotherNewFolder")]
        public void InitActionTest(string folderName)
        {
            var localWorkingDirectory = string.Empty;
            // Test
            if (string.IsNullOrEmpty(folderName))
            {
                Program.Main(new[] { "init" });
                localWorkingDirectory = WorkingDirectory;
            }
            else
            {
                Program.Main(new[] { "init", folderName });
                localWorkingDirectory = Path.Combine(WorkingDirectory, folderName);
            }

            var files = Directory.GetFiles(localWorkingDirectory).Select(Path.GetFileName);
            var folders = Directory.GetDirectories(localWorkingDirectory).Select(Path.GetFileName);

            // Assert
            files.Should().HaveCount(3);
            files.Should().Contain(".gitignore");
            files.Should().Contain("host.json");
            files.Should().Contain("local.settings.json");

            var expectedFolders = 2;
            folders
                .Should()
                .HaveCount(expectedFolders,
                $"Expected to have {expectedFolders}, but got {folders.Count()}, which are {folders.Aggregate(string.Empty, (a, b) => string.Join(",", a, b))}");
            folders.Should().Contain(".git");
        }
    }
}
