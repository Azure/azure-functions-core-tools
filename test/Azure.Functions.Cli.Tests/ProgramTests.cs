using Azure.Functions.Cli;
using FluentAssertions;
using System.Text;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class ProgramTests
    {
        [Fact]
        public void ConsoleOutputEncodingShouldBeUtf8()
        {
            // Act
            var encoding = Program.GetConsoleOutputEncoding();
            
            // Assert
            encoding.Should().Be(Encoding.UTF8);
        }
    }
}