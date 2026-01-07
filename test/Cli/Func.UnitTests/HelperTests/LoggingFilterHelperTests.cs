// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
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

            var testConfiguration = TestUtilities.CreateSetupWithConfiguration(settings);
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, verboseLogging);
            if (verboseLogging == null)
            {
                Assert.False(loggingFilterHelper.VerboseLogging);
            }

            if (!string.IsNullOrEmpty(categoryKey) && categoryKey.Equals("Default", StringComparison.OrdinalIgnoreCase))
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
                    Environment.SetEnvironmentVariable(LoggingFilterHelper.CiBuildNumber, "90l99");
                }

                var testConfiguration = TestUtilities.CreateSetupWithConfiguration(null);
                LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, verboseLogging);
                Assert.Equal(expected, loggingFilterHelper.IsCiEnvironment(verboseLogging.HasValue));
            }
            finally
            {
                Environment.SetEnvironmentVariable(LoggingFilterHelper.CiBuildNumber, string.Empty);
            }
        }

        [Theory]
        [InlineData("Debug", LogLevel.Debug)]
        [InlineData("Information", LogLevel.Information)]
        [InlineData("Warning", LogLevel.Warning)]
        [InlineData("Error", LogLevel.Error)]
        public void UserLogLevel_CommandLineParameter_Tests(string userLogLevelString, LogLevel expectedUserLogLevel)
        {
            var testConfiguration = TestUtilities.CreateSetupWithConfiguration(null);
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, null, userLogLevelString);
            
            Assert.Equal(expectedUserLogLevel, loggingFilterHelper.UserLogDefaultLogLevel);
            // System log level should remain at the default
            Assert.Equal(LogLevel.Warning, loggingFilterHelper.SystemLogDefaultLogLevel);
        }

        [Fact]
        public void UserLogLevel_EnvironmentVariable_Tests()
        {
            try
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_USER_LOG_LEVEL", "Debug");
                
                var testConfiguration = TestUtilities.CreateSetupWithConfiguration(null);
                LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, null);
                
                Assert.Equal(LogLevel.Debug, loggingFilterHelper.UserLogDefaultLogLevel);
                // System log level should remain at the default
                Assert.Equal(LogLevel.Warning, loggingFilterHelper.SystemLogDefaultLogLevel);
            }
            finally
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_USER_LOG_LEVEL", string.Empty);
            }
        }

        [Fact]
        public void UserLogLevel_CommandLineOverridesEnvironmentVariable_Tests()
        {
            try
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_USER_LOG_LEVEL", "Debug");
                
                var testConfiguration = TestUtilities.CreateSetupWithConfiguration(null);
                // CLI parameter should override environment variable
                LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, null, "Error");
                
                Assert.Equal(LogLevel.Error, loggingFilterHelper.UserLogDefaultLogLevel);
                Assert.Equal(LogLevel.Warning, loggingFilterHelper.SystemLogDefaultLogLevel);
            }
            finally
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_USER_LOG_LEVEL", string.Empty);
            }
        }

        [Fact]
        public void UserLogLevel_DoesNotOverrideHostJsonDefault_WhenNotSpecified_Tests()
        {
            var settings = new Dictionary<string, string>();
            settings.Add(ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "logging", "loglevel", "Default"), LogLevel.Debug.ToString());
            
            var testConfiguration = TestUtilities.CreateSetupWithConfiguration(settings);
            // Without specifying userLogLevel, both should be set to the host.json default
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, null);
            
            Assert.Equal(LogLevel.Debug, loggingFilterHelper.UserLogDefaultLogLevel);
            Assert.Equal(LogLevel.Debug, loggingFilterHelper.SystemLogDefaultLogLevel);
        }

        [Fact]
        public void UserLogLevel_OverridesHostJsonDefault_WhenSpecified_Tests()
        {
            var settings = new Dictionary<string, string>();
            settings.Add(ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "logging", "loglevel", "Default"), LogLevel.Debug.ToString());
            
            var testConfiguration = TestUtilities.CreateSetupWithConfiguration(settings);
            // When specifying userLogLevel, it should override the host.json default for user logs only
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(testConfiguration, null, "Error");
            
            Assert.Equal(LogLevel.Error, loggingFilterHelper.UserLogDefaultLogLevel);
            Assert.Equal(LogLevel.Debug, loggingFilterHelper.SystemLogDefaultLogLevel);
        }
    }
}
