using Azure.Functions.Cli.Helpers;
using Fclp.Internals;
using Microsoft.ApplicationInsights;
using Moq;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Azure.Functions.Cli.Tests.HelpersTests
{
    public class TelemetryHelpersTests
    {
        private static Mock<ICommandLineOption> SetupCommand(string longName, string shortName, bool hasLongName, bool hasShortName)
        {
            var mockCommand = new Mock<ICommandLineOption>();

            mockCommand
                .Setup(m => m.LongName)
                .Returns(longName);

            mockCommand
                .Setup(m => m.ShortName)
                .Returns(shortName);

            mockCommand
                .Setup(m => m.HasLongName)
                .Returns(hasLongName);

            mockCommand
                .Setup(m => m.HasShortName)
                .Returns(hasShortName);

            return mockCommand;
        }

        [Fact]
        public void GetCommandsFromCommandLineOptions()
        {
            var mockForce = new Mock<ICommandLineOption>();
            mockForce
                .Setup(m => m.HasLongName)
                .Returns(true);
                
            var cliOptions = new List<ICommandLineOption>
            {
                SetupCommand(longName : "force", shortName : "f", hasLongName : true, hasShortName : true).Object,
                SetupCommand(longName : "verbose", shortName : "v", hasLongName : true, hasShortName : true).Object,
                SetupCommand(longName : null, shortName : "e", hasLongName : false, hasShortName : true).Object,
                SetupCommand(longName : "port", shortName : "p", hasLongName : false, hasShortName : true).Object
            };

            var commands = TelemetryHelpers.GetCommandsFromCommandLineOptions(cliOptions);

            Assert.Contains("force", commands);
            Assert.Contains("verbose", commands);
            Assert.Contains("e", commands);
            Assert.Contains("p", commands);
            Assert.Equal(4, commands.Count());
        }
    }
}
