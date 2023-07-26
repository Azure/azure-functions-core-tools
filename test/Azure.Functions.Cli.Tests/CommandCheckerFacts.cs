using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class CommandCheckerFacts
    {
        [Fact]
        public void CommandCheckerShouldWork()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var exists = CommandChecker.CommandExists("cmd", out _);
                var doesntExist = CommandChecker.CommandExists("fooo", out _);

                exists.Should().BeTrue(because: "checking if cmd command exists should always be true on Windows");
                doesntExist.Should().BeFalse(because: "checking if fooo command exists on windows should be false");
            }
            else
            {
                var exists = CommandChecker.CommandExists("bash", out _);
                var doesntExist = CommandChecker.CommandExists("fooo", out _);

                exists.Should().BeTrue(because: "checking if sh command exists should always be true on Unix-like");
                doesntExist.Should().BeFalse(because: "checking if fooo command exists on Unix-like should be false");
            }
        }
    }
}
