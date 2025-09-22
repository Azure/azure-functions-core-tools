// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Build.Logging;

namespace Azure.Functions.Cli.Common
{
    internal class SecretsManager : ISecretsManager
    {
        private static readonly Lazy<AppSettingsFile> _lazySettings = new Lazy<AppSettingsFile>(() => new AppSettingsFile(AppSettingsFilePath));

        private static AppSettingsFile Settings => _lazySettings.Value;

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

                ColoredConsole.WriteLine($"{secretsFile} found in root directory ({rootPath}).");
                return secretsFilePath;
            }
        }

        public static string AppSettingsFileName
        {
            get
            {
                return Path.GetFileName(AppSettingsFilePath);
            }
        }

        public IDictionary<string, string> GetSecrets(bool refreshSecrets = false)
        {
            if (refreshSecrets)
            {
                return new AppSettingsFile(AppSettingsFilePath).GetValues();
            }

            return Settings.GetValues();
        }

        public IEnumerable<ConnectionString> GetConnectionStrings()
        {
            return Settings.GetConnectionStrings();
        }

        public void SetSecret(string name, string value)
        {
            Settings.SetSecret(name, value);
            Settings.Commit();
        }

        public void SetConnectionString(string name, string value)
        {
            Settings.SetConnectionString(name, value, Constants.DefaultSqlProviderName);
            Settings.Commit();
        }

        public void DecryptSettings()
        {
            if (Settings.IsEncrypted)
            {
                var values = Settings.GetValues();
                var connectionStrings = Settings.GetConnectionStrings();
                Settings.IsEncrypted = false;

                foreach (var pair in values)
                {
                    Settings.SetSecret(pair.Key, pair.Value);
                }

                foreach (var connectionString in connectionStrings)
                {
                    Settings.SetConnectionString(connectionString.Name, connectionString.Value, connectionString.ProviderName);
                }

                Settings.Commit();
            }
        }

        public void EncryptSettings()
        {
            if (!Settings.IsEncrypted)
            {
                var values = Settings.GetValues();
                var connectionStrings = Settings.GetConnectionStrings();
                Settings.IsEncrypted = true;

                foreach (var pair in values)
                {
                    Settings.SetSecret(pair.Key, pair.Value);
                }

                foreach (var connectionString in connectionStrings)
                {
                    Settings.SetConnectionString(connectionString.Name, connectionString.Value, connectionString.ProviderName);
                }

                Settings.Commit();
            }
        }

        public void DeleteSecret(string name)
        {
            Settings.RemoveSetting(name);
            Settings.Commit();
        }

        public HostStartSettings GetHostStartSettings()
        {
            try
            {
                return Settings?.Host ?? new HostStartSettings();
            }
            catch (CliException)
            {
                return new HostStartSettings();
            }
        }

        public void DeleteConnectionString(string name)
        {
            Settings.RemoveConnectionString(name);
            Settings.Commit();
        }
    }
}
