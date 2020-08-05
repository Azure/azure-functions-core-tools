using System;
using System.Collections.Generic;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class UserSecretsConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        private readonly string _scriptPath;

        public UserSecretsConfigurationBuilder(string scriptPath)
        {
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
            string userSecretsId = GetUserSecretsId(_scriptPath);
            if (userSecretsId == null) return;

            builder.AddUserSecrets(userSecretsId);
        }

        private string GetUserSecretsId(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath)) return null;

            string projectFilePath = ProjectHelpers.FindProjectFile(scriptPath);
            if (projectFilePath == null) return null;

            var projectRoot = ProjectHelpers.GetProject(projectFilePath);
            var userSecretsId = ProjectHelpers.GetPropertyValue(projectRoot, Constants.UserSecretsIdElementName);

            return userSecretsId;
        }
    }
}