using System;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class UserSecretsConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        private readonly string _scriptPath;
        private readonly LoggingFilterHelper _loggingFilterHelper;

        public UserSecretsConfigurationBuilder(string scriptPath, LoggingFilterHelper loggingFilterHelper)
        {
            _loggingFilterHelper = loggingFilterHelper;
            if (string.IsNullOrEmpty(scriptPath))
            {
                _scriptPath = Environment.CurrentDirectory;
            }
            else
            {
                _scriptPath = scriptPath;
            }
        }

        public void Configure(IConfigurationBuilder builder)
        {
            string userSecretsId = GetUserSecretsId();
            if (userSecretsId == null)
            {
                return;
            }
            builder.AddUserSecrets(userSecretsId);
        }

        private string GetUserSecretsId()
        {
            if (string.IsNullOrEmpty(_scriptPath))
            {
                return null;
            }
            string projectFilePath = ProjectHelpers.FindProjectFile(_scriptPath, _loggingFilterHelper);
            if (projectFilePath == null) return null;

            var projectRoot = ProjectHelpers.GetProject(projectFilePath);
            var userSecretsId = ProjectHelpers.GetPropertyValue(projectRoot, Constants.UserSecretsIdElementName);

            return userSecretsId;
        }
    }
}