using Azure.Functions.Cli.Diagnostics;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class ColoredConsoleLoggerTests
    {
        private IConfigurationRoot _testConfiguration;

        public ColoredConsoleLoggerTests()
        {
            string defaultJson = "{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}";
            _testConfiguration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.ASCII.GetBytes(defaultJson))).Build();
        }

        [Theory]
        [InlineData("somelog", false)]
        [InlineData("Worker process started and initialized.", true)]
        [InlineData("Worker PROCESS started and initialized.", true)]
        [InlineData("Worker process started.", false)]
        [InlineData("Host lock lease acquired by instance ID", true)]
        [InlineData("Host lock lease acquired by instance id", true)]
        [InlineData("Host lock lease", false)]
        public void DoesMessageStartsWithWhiteListedPrefix_Tests(string formattedMessage, bool expected)
        {
            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger("test", new LoggingFilterHelper(_testConfiguration, true));
            Assert.Equal(expected, coloredConsoleLogger.DoesMessageStartsWithAllowedLogsPrefix(formattedMessage));
        }

        [Theory]
        [InlineData("somelog", false)]
        [InlineData("Worker process started and initialized.", true)]
        [InlineData("Worker PROCESS started and initialized.", true)]
        [InlineData("Worker process started.", false)]
        [InlineData("Host lock lease acquired by instance ID", true)]
        [InlineData("Host lock lease acquired by instance id", true)]
        [InlineData("Host lock lease", false)]
        public void IsEnabled_Tests(string formattedMessage, bool expected)
        {
            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger("test", new LoggingFilterHelper(_testConfiguration, true));
            Assert.Equal(expected, coloredConsoleLogger.DoesMessageStartsWithAllowedLogsPrefix(formattedMessage));
        }
    }
}
