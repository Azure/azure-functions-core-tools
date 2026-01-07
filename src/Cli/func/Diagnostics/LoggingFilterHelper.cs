// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli
{
    public class LoggingFilterHelper
    {
        // CI EnvironmentSettings
        // https://github.com/watson/ci-info/blob/master/index.js#L52-L59
        public const string Ci = "CI"; // Travis CI, CircleCI, Cirrus CI, Gitlab CI, Appveyor, CodeShip, dsari
        public const string CiContinuousIntegration = "CONTINUOUS_INTEGRATION";  // Travis CI, Cirrus CI
        public const string CiBuildNumber = "BUILD_NUMBER";  // Travis CI, Cirrus CI
        public const string CiRunId = "RUN_ID"; // TaskCluster, dsari

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

            // Check for environment variable if userLogLevel is not provided via CLI
            if (string.IsNullOrEmpty(userLogLevel))
            {
                userLogLevel = Environment.GetEnvironmentVariable("FUNCTIONS_USER_LOG_LEVEL");
            }

            // Track if userLogLevel was explicitly set and successfully parsed
            bool userLogLevelExplicitlySet = false;

            // If userLogLevel is specified (via CLI or env var), use it
            if (!string.IsNullOrEmpty(userLogLevel) && Enum.TryParse<LogLevel>(userLogLevel, true, out LogLevel parsedUserLogLevel))
            {
                UserLogDefaultLogLevel = parsedUserLogLevel;
                userLogLevelExplicitlySet = true;
            }

            if (Utilities.LogLevelExists(hostJsonConfig, Utilities.LogLevelDefaultSection, out LogLevel logLevel))
            {
                SystemLogDefaultLogLevel = logLevel;
                // Only override UserLogDefaultLogLevel if it wasn't explicitly set via CLI or env var
                if (!userLogLevelExplicitlySet)
                {
                    UserLogDefaultLogLevel = logLevel;
                }
            }
        }

        /// <summary>
        /// Gets default level for system logs.
        /// </summary>
        public LogLevel SystemLogDefaultLogLevel { get; private set; } = LogLevel.Warning;

        /// <summary>
        /// Gets default level for user logs.
        /// </summary>
        public LogLevel UserLogDefaultLogLevel { get; private set; } = LogLevel.Information;

        /// <summary>
        /// Gets a value indicating whether is set to true if `func start` is started with `--verbose` flag. If set, SystemLogDefaultLogLevel is set to Information.
        /// </summary>
        public bool VerboseLogging { get; private set; }

        internal bool IsCiEnvironment(bool verboseLoggingArgExists)
        {
            if (verboseLoggingArgExists)
            {
                return VerboseLogging;
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Ci)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CiContinuousIntegration)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CiBuildNumber)) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CiRunId)))
            {
                return true;
            }

            return false;
        }
    }
}
