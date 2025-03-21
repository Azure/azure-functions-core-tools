using System;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class UserSecretsConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        private readonly string _userSecretsId;
        private readonly LoggingFilterHelper _loggingFilterHelper;
        private readonly LoggerFilterOptions _loggerFilterOptions;

        public UserSecretsConfigurationBuilder(string userSecretsId, LoggingFilterHelper loggingFilterHelper, LoggerFilterOptions loggerFilterOptions)
        {
            _loggingFilterHelper = loggingFilterHelper;
            _loggerFilterOptions = loggerFilterOptions;
            _userSecretsId = userSecretsId;
        }

        public void Configure(IConfigurationBuilder builder)
        {
            if (_userSecretsId == null)
            {
                return;
            }
            builder.AddUserSecrets(_userSecretsId);
        }
    }
}