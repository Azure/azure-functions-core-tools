using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Xunit;
using System.Collections.Generic;

namespace Azure.Functions.Cli.Tests
{
    public class ColoredConsoleLoggerTests
    {
        IConfigurationRoot _testConfiguration;

        public ColoredConsoleLoggerTests()
        {
            _testConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:Logging:LogLevel:Host.Startup", "Debug" }
                }).Build();
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
        public void IsEnabled_Tests(string formattedMessage, bool expected)
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
