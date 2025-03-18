﻿using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Dynamitey;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Azure.Functions.Cli
{
    public class LoggingFilterHelper
    {
        // CI EnvironmentSettings
        // https://github.com/watson/ci-info/blob/master/index.js#L52-L59
        public const string Ci = "CI"; // Travis CI, CircleCI, Cirrus CI, Gitlab CI, Appveyor, CodeShip, dsari
        public const string Ci_Continuous_Integration = "CONTINUOUS_INTEGRATION";  // Travis CI, Cirrus CI
        public const string Ci_Build_Number = "BUILD_NUMBER";  // Travis CI, Cirrus CI
        public const string Ci_Run_Id = "RUN_ID"; // TaskCluster, dsari
        public static readonly string[] ValidUserLogLevels = ["Trace", "Debug", "Information", "Warning", "Error","Critical", "None"];

        public LoggingFilterHelper(IConfigurationRoot hostJsonConfig, bool? verboseLogging, string userLogLevel = null)
        {
            VerboseLogging = verboseLogging.HasValue && verboseLogging.Value;

            if (IsCiEnvironment(verboseLogging.HasValue))
            {
                VerboseLogging = true;
            }
            if (VerboseLogging)
            {
                SystemLogDefaultLogLevel = LogLevel.Information;
            }
            if (Utilities.LogLevelExists(hostJsonConfig, Utilities.LogLevelDefaultSection, out LogLevel logLevel))
            {
                SystemLogDefaultLogLevel = logLevel;
            }

            // Check for user log level
            if (!string.IsNullOrEmpty(userLogLevel))
            { 
                ValidateUserLogLevel(userLogLevel);
                if (Enum.TryParse(userLogLevel, true, out LogLevel UserLogLevel))
                {
                    UserLogDefaultLogLevel = UserLogLevel;
                }
            }
        }

        /// <summary>
        /// Default level for system logs
        /// </summary>
        public LogLevel SystemLogDefaultLogLevel { get; } = LogLevel.Warning;

        /// <summary>
        /// Default level for user logs
        /// </summary>
        public LogLevel UserLogDefaultLogLevel { get; } = LogLevel.Information;

        /// <summary>
        /// Is set to true if `func start` is started with `--verbose` flag. If set, SystemLogDefaultLogLevel is set to Information
        /// </summary>
        public bool VerboseLogging { get; private set; }

        internal bool IsCiEnvironment(bool verboseLoggingArgExists)
        {
            if (verboseLoggingArgExists)
            {
                return VerboseLogging;
            }
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Ci)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Ci_Continuous_Integration)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Ci_Build_Number)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Ci_Run_Id)))
            {
                return true;
            }
            return false;
        }
        private void ValidateUserLogLevel(string UserLogLevel)
        {
            if (LoggingFilterHelper.ValidUserLogLevels.Contains(UserLogLevel, StringComparer.OrdinalIgnoreCase) == false)
            {
                throw new CliException($"The userLogLevel value provided, '{UserLogLevel}', is invalid. Valid values are '{string.Join("', '", LoggingFilterHelper.ValidUserLogLevels)}'.");
            }
        }
    }
}
