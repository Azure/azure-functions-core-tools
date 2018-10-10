using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Helpers;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Common
{
    internal class SecretsManager : ISecretsManager
    {
        public static string AppSettingsFilePath
        {
            get
            {
                var secretsFile = "local.settings.json";
                var rootPath = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory, new List<string>
                {
                    ScriptConstants.HostMetadataFileName,
                    secretsFile,
                });
                var secretsFilePath = Path.Combine(rootPath, secretsFile);
                return secretsFilePath;
            }
        }

        public static string MiddlewareAuthSettingsFilePath
        {
            get
            {
                var authFile = "local.middleware.json";
                var rootPath = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory, new List<string>
                {
                    ScriptConstants.HostMetadataFileName,
                    authFile,
                });
                var authFilePath = Path.Combine(rootPath, authFile);
                return authFilePath;
            }
        }

        public static string AppSettingsFileName
        {
            get
            {
                return Path.GetFileName(AppSettingsFilePath);
            }
        }

        public static string MiddlewareAuthSettingsFileName
        {
            get
            {
                return Path.GetFileName(MiddlewareAuthSettingsFilePath);
            }
        }

        public IDictionary<string, string> GetSecrets()
        {
            var appSettingsFile = new AppSettingsFile(AppSettingsFilePath);
            return appSettingsFile.GetValues();
        }

        public IDictionary<string, string> GetMiddlewareAuthSettings()
        {
            var authSettingsFile = new AuthSettingsFile(MiddlewareAuthSettingsFilePath);
            return authSettingsFile.GetValues();
        }

        public IEnumerable<ConnectionString> GetConnectionStrings()
        {
            var appSettingsFile = new AppSettingsFile(AppSettingsFilePath);
            return appSettingsFile.GetConnectionStrings();
        }

        public void SetSecret(string name, string value)
        {
            var appSettingsFile = new AppSettingsFile(AppSettingsFilePath);
            appSettingsFile.SetSecret(name, value);
            appSettingsFile.Commit();
        }

        public void SetConnectionString(string name, string value)
        {
            var appSettingsFile = new AppSettingsFile(AppSettingsFilePath);
            appSettingsFile.SetConnectionString(name, value, Constants.DefaultSqlProviderName);
            appSettingsFile.Commit();
        }

        public void DecryptSettings()
        {
            var settingsFile = new AppSettingsFile(AppSettingsFilePath);
            if (settingsFile.IsEncrypted)
            {
                var values = settingsFile.GetValues();
                var connectionStrings = settingsFile.GetConnectionStrings();
                settingsFile.IsEncrypted = false;

                foreach (var pair in values)
                {
                    settingsFile.SetSecret(pair.Key, pair.Value);
                }

                foreach (var connectionString in connectionStrings)
                {
                    settingsFile.SetConnectionString(connectionString.Name, connectionString.Value, connectionString.ProviderName);
                }

                settingsFile.Commit();
            }
        }

        public void EncryptSettings()
        {
            var settingsFile = new AppSettingsFile(AppSettingsFilePath);
            if (!settingsFile.IsEncrypted)
            {
                var values = settingsFile.GetValues();
                var connectionStrings = settingsFile.GetConnectionStrings();
                settingsFile.IsEncrypted = true;

                foreach (var pair in values)
                {
                    settingsFile.SetSecret(pair.Key, pair.Value);
                }

                foreach (var connectionString in connectionStrings)
                {
                    settingsFile.SetConnectionString(connectionString.Name, connectionString.Value, connectionString.ProviderName);
                }

                settingsFile.Commit();

            }
        }

        public void DeleteSecret(string name)
        {
            var settingsFile = new AppSettingsFile(AppSettingsFilePath);
            settingsFile.RemoveSetting(name);
            settingsFile.Commit();
        }

        public HostStartSettings GetHostStartSettings()
        {
            var settingsFile = new AppSettingsFile(AppSettingsFilePath);
            return settingsFile.Host ?? new HostStartSettings();
        }

        public void DeleteConnectionString(string name)
        {
            var settingsFile = new AppSettingsFile(AppSettingsFilePath);
            settingsFile.RemoveConnectionString(name);
            settingsFile.Commit();
        }
    }
}
