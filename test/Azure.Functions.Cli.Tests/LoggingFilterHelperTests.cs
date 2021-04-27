using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class LoggingFilterHelperTests
    {
        [Theory]
        [InlineData("default", true, LogLevel.None)]
        [InlineData("Default", true, LogLevel.Debug)]
        [InlineData(null, null, LogLevel.Information)]
        [InlineData("Host.Startup", true, LogLevel.Information)]
        public void LoggingFilterHelper_Tests(string categoryKey, bool? verboseLogging, LogLevel expectedDefaultLogLevel)
        {

            var settings = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(categoryKey))
            {
                settings.Add(ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "logging", "loglevel", categoryKey), expectedDefaultLogLevel.ToString());
            }
            var testConfiguration = TestUtils.CreateSetupWithConfiguration(settings);
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, verboseLogging);
            if (verboseLogging == null)
            {
                Assert.False(loggingFilterHelper.VerboseLogging);
            }
            if ( !string.IsNullOrEmpty(categoryKey) && categoryKey.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(expectedDefaultLogLevel, loggingFilterHelper.UserLogDefaultLogLevel);
                Assert.Equal(expectedDefaultLogLevel, loggingFilterHelper.SystemLogDefaultLogLevel);
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
        }

        [Theory]
        [InlineData(false, null, false)]
        [InlineData(true, false, false)]
        [InlineData(true, null, true)]
        public void IsCI_Tests(bool isCiEnv, bool? verboseLogging, bool expected)
        {
            try
            {
                if (isCiEnv)
                {
                    Environment.SetEnvironmentVariable(LoggingFilterHelper.Ci_Build_Number, "90l99");
                }
                var testConfiguration = TestUtils.CreateSetupWithConfiguration(null);
                LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, verboseLogging);
                Assert.Equal(expected, loggingFilterHelper.IsCiEnvironment(verboseLogging.HasValue));
               
            }
            finally
            {
                Environment.SetEnvironmentVariable(LoggingFilterHelper.Ci_Build_Number, "");
            }
        }
    }
}
