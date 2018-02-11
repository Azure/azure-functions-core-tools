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
        private const string reason = "secrets.manager.1";

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

        public static string AppSettingsFileName
        {
            get
            {
                return Path.GetFileName(AppSettingsFilePath);
            }
        }

        public IDictionary<string, string> GetSecrets()
        {
            var appSettingsFile = new AppSettingsFile(AppSettingsFilePath);
            return appSettingsFile.GetValues();
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

        private class AppSettingsFile
        {
            private readonly string _filePath;

            public AppSettingsFile(string filePath)
            {
                _filePath = filePath;
                try
                {
                    var content = FileSystemHelpers.ReadAllTextFromFile(_filePath);
                    var appSettings = JsonConvert.DeserializeObject<AppSettingsFile>(content);
                    IsEncrypted = appSettings.IsEncrypted;
                    Values = appSettings.Values;
                    ConnectionStrings = appSettings.ConnectionStrings;
                    Host = appSettings.Host;
                }
                catch
                {
                    Values = new Dictionary<string, string>();
                    ConnectionStrings = new Dictionary<string, JToken>();
                    IsEncrypted = true;
                }
            }

            public bool IsEncrypted { get; set; }
            public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, JToken> ConnectionStrings { get; set; } = new Dictionary<string, JToken>();

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public HostStartSettings Host { get; set; }

            public void SetSecret(string name, string value)
            {
                if (IsEncrypted)
                {
                    Values[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.Default.GetBytes(value), reason));
                }
                else
                {
                    Values[name] = value;
                };
            }

            public void SetConnectionString(string name, string value, string providerName)
            {
                value = IsEncrypted
                    ? Convert.ToBase64String(ProtectedData.Protect(Encoding.Default.GetBytes(value), reason))
                    : value;

                ConnectionStrings[name] = JToken.FromObject(new
                {
                    ConnectionString = value,
                    ProviderName = providerName
                });
            }

            public void RemoveSetting(string name)
            {
                if (Values.ContainsKey(name))
                {
                    Values.Remove(name);
                }
            }

            public void RemoveConnectionString(string name)
            {
                if (ConnectionStrings.ContainsKey(name))
                {
                    ConnectionStrings.Remove(name);
                }
            }

            public void Commit()
            {
                FileSystemHelpers.WriteAllTextToFile(_filePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }

            public IDictionary<string, string> GetValues()
            {
                if (IsEncrypted)
                {
                    try
                    {
                        return Values.ToDictionary(k => k.Key, v => string.IsNullOrEmpty(v.Value) ? string.Empty : Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), reason)));
                    }
                    catch (Exception e)
                    {
                        throw new CliException("Failed to decrypt settings. Encrypted settings only be edited through 'func settings add'.", e);
                    }
                }
                else
                {
                    return Values.ToDictionary(k => k.Key, v => v.Value);
                }
            }

            public IEnumerable<ConnectionString> GetConnectionStrings()
            {
                try
                {
                    string DecryptIfNeeded(string value) => IsEncrypted
                        ? Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), reason))
                        : value;

                    return ConnectionStrings.Select(c =>
                    {
                        return c.Value.Type == JTokenType.String
                            ? new ConnectionString
                            {
                                Name = c.Key,
                                Value = DecryptIfNeeded(c.Value.ToString()),
                                ProviderName = Constants.DefaultSqlProviderName
                            }
                            : new ConnectionString
                            {
                                Name = c.Key,
                                Value = DecryptIfNeeded(c.Value["ConnectionString"]?.ToString()),
                                ProviderName = c.Value["ProviderName"]?.ToString()
                            };
                    })
                    .ToList();
                }
                catch (Exception e)
                {
                    throw new CliException("Failed to decrypt settings. Encrypted settings only be edited through 'func settings add'.", e);
                }
            }
        }
    }
}
