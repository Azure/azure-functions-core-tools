using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class LoggingFilterHelperTests
    {
        private ScriptApplicationHostOptions _hostOptions;

        public LoggingFilterHelperTests()
        {
            _hostOptions = new ScriptApplicationHostOptions
            {
                ScriptPath = Directory.GetCurrentDirectory()
            };
        }

        [Theory]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Default\": \"None\"}}}", true, LogLevel.None)]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Default\": \"DEBUG\"}}}", true, LogLevel.Debug)]
        [InlineData("{\"version\": \"2.0\"}", null, LogLevel.Information)]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}", true, LogLevel.Information)]
        public void LoggingFilterHelper_Tests(string hostJsonContent, bool? verboseLogging, LogLevel expectedDefaultLogLevel)
        {
            FileSystemHelpers.WriteAllTextToFile(Constants.HostJsonFileName, hostJsonContent);
            var configuration = Utilities.BuildHostJsonConfigutation(_hostOptions);
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(configuration, verboseLogging);
            if (verboseLogging == null)
            {
                Assert.False(loggingFilterHelper.VerboseLogging);
            }
            Assert.Equal(loggingFilterHelper.DefaultLogLevel, expectedDefaultLogLevel);
            if (hostJsonContent.Contains("Default"))
            {
                Assert.Equal(loggingFilterHelper.DefaultLogLevel, loggingFilterHelper.UserLogDefaultLogLevel);
                Assert.Equal(loggingFilterHelper.DefaultLogLevel, loggingFilterHelper.SystemLogDefaultLogLevel);
            }
            else
            {
                Assert.Equal(LogLevel.Information, loggingFilterHelper.UserLogDefaultLogLevel);
                if (verboseLogging.HasValue && verboseLogging.Value)
                {
                    Assert.Equal(LogLevel.Information, loggingFilterHelper.SystemLogDefaultLogLevel);
                }
                else
                {
                    Assert.Equal(LogLevel.Warning, loggingFilterHelper.SystemLogDefaultLogLevel);
                }
            }
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                loggingFilterHelper.AddConsoleLoggingProvider(builder);
                var serviceProvider = builder.Services.BuildServiceProvider();
                var coloredConsoleLoggerProvider = (ColoredConsoleLoggerProvider)serviceProvider.GetService<ILoggerProvider>();
                Assert.NotNull(coloredConsoleLoggerProvider);
            });
        }

        [Theory]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Default\": \"None\"}}}", "test", LogLevel.Information, false)]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}", "Host.Startup", LogLevel.Information, true)]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}", "Host.General", LogLevel.Information, false)]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}", "Host.General", LogLevel.Warning, true)]
        public void IsEnabled_Tests(string hostJsonContent, string category, LogLevel logLevel, bool expected)
        {
            FileSystemHelpers.WriteAllTextToFile(Constants.HostJsonFileName, hostJsonContent);
            var configuration = Utilities.BuildHostJsonConfigutation(_hostOptions);
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(configuration, false);
            Assert.Equal(expected, loggingFilterHelper.IsEnabled(category, logLevel));
        }

        [Theory]
        [InlineData(false, null, false)]
        [InlineData(true, false, false)]
        [InlineData(true, null, true)]
        public void IsCI_Tests(bool isCiEnv, bool? verboseLogging, bool expected)
        {
            if (isCiEnv)
            {
                Environment.SetEnvironmentVariable(LoggingFilterHelper.Ci_Build_Number, "90l99");
            }
            string defaultJson = "{\"version\": \"2.0\"}";
            FileSystemHelpers.WriteAllTextToFile(Constants.HostJsonFileName, defaultJson);
            var configuration = Utilities.BuildHostJsonConfigutation(_hostOptions);
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(configuration, verboseLogging);
            Assert.Equal(expected, loggingFilterHelper.IsCiEnvironment(verboseLogging.HasValue));
            Environment.SetEnvironmentVariable(LoggingFilterHelper.Ci_Build_Number, "");
        }
    }
}
