using Azure.Functions.Cli.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using System;

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
            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger("test", new LoggingFilterHelper(_testConfiguration, true), new LoggerFilterOptions());
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
        public void DoesMessageStartsWithAllowedLogsPrefix_Tests(string formattedMessage, bool expected)
        {
            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger("test", new LoggingFilterHelper(_testConfiguration, true), new LoggerFilterOptions());
            Assert.Equal(expected, coloredConsoleLogger.DoesMessageStartsWithAllowedLogsPrefix(formattedMessage));
        }

        [Theory]
        [InlineData("Random", LogLevel.Information, true)]
        [InlineData("Random", LogLevel.Debug, false)]
        [InlineData("Host.Startup", LogLevel.Information, true)]
        [InlineData("Host.Startup", LogLevel.Trace, false)]
        [InlineData("Host.General", LogLevel.Trace, false)]
        [InlineData("Host.General", LogLevel.Debug, false)]
        [InlineData("Host.General", LogLevel.Information, false)]
        public void IsEnabled_LoggerFilterTests_Tests(string inputCategory, LogLevel inputLogLevel, bool expected)
        {
            LoggerFilterRule loggerFilterRule1 = new LoggerFilterRule(null, "Host.", LogLevel.None, null);
            LoggerFilterRule loggerFilterRule2 = new LoggerFilterRule(null, "Host.Startup", LogLevel.Debug, null);

            LoggerFilterOptions customFilterOptions = new LoggerFilterOptions();
            customFilterOptions.MinLevel = LogLevel.Information;
            customFilterOptions.Rules.Add(loggerFilterRule1);
            customFilterOptions.Rules.Add(loggerFilterRule2);

            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger(inputCategory, new LoggingFilterHelper(_testConfiguration, true), customFilterOptions);
            bool result = coloredConsoleLogger.IsEnabled(inputCategory, inputLogLevel);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SelectRule_Tests()
        {
            List<LoggerFilterRule> loggerFilterRules = new List<LoggerFilterRule>();
            LoggerFilterRule loggerFilterRule = new LoggerFilterRule(null, "Host.", LogLevel.Trace, null);
            loggerFilterRules.Add(loggerFilterRule);

            LoggerFilterOptions customFilterOptions = new LoggerFilterOptions();
            customFilterOptions.MinLevel = LogLevel.Information;
            customFilterOptions.Rules.Add(loggerFilterRule);

            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger("test", new LoggingFilterHelper(_testConfiguration, true), customFilterOptions);
            var startupLogsRule = coloredConsoleLogger.SelectRule("Host.Startup", customFilterOptions);
            Assert.NotNull(startupLogsRule.LogLevel);
            Assert.Equal(LogLevel.Trace, startupLogsRule.LogLevel);

            var functionLogsRule = coloredConsoleLogger.SelectRule("Function.TestFunction", customFilterOptions);
            Assert.NotNull(functionLogsRule.LogLevel);
            Assert.Equal(LogLevel.Information, functionLogsRule.LogLevel);
        }
    }
}
