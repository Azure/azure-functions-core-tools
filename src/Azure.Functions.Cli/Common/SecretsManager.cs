using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Common
{
    internal class SecretsManager : ISecretsManager
    {
        public const string AppSettingsFileName = "appsettings.json";

        public IDictionary<string, string> GetSecrets()
        {
            var appSettingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            return appSettingsFile.GetValues();
        }

        public IDictionary<string, string> GetConnectionStrings()
        {
            var appSettingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            return appSettingsFile.GetConnectionStrings();
        }

        public void SetSecret(string name, string value)
        {
            var appSettingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            appSettingsFile.SetSecret(name, value);
            appSettingsFile.Commit();
        }

        public void SetConnectionString(string name, string value)
        {
            var appSettingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            appSettingsFile.SetConnectionString(name, value);
            appSettingsFile.Commit();
        }

        public void DecryptSettings()
        {
            var settingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            if (settingsFile.IsEncrypted)
            {
                var values = settingsFile.GetValues();
                var connectionStrings = settingsFile.GetConnectionStrings();
                settingsFile.IsEncrypted = false;

                foreach (var pair in values)
                {
                    settingsFile.SetSecret(pair.Key, pair.Value);
                }

                foreach(var pair in connectionStrings)
                {
                    settingsFile.SetConnectionString(pair.Key, pair.Value);
                }

                settingsFile.Commit();
            }
        }

        public void EncryptSettings()
        {
            var settingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            if (!settingsFile.IsEncrypted)
            {
                var values = settingsFile.GetValues();
                var connectionStrings = settingsFile.GetConnectionStrings();
                settingsFile.IsEncrypted = true;

                foreach (var pair in values)
                {
                    settingsFile.SetSecret(pair.Key, pair.Value);
                }

                foreach(var pair in connectionStrings)
                {
                    settingsFile.SetConnectionString(pair.Key, pair.Value);
                }

                settingsFile.Commit();

            }
        }

        public void DeleteSecret(string name)
        {
            var settingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            settingsFile.RemoveSetting(name);
            settingsFile.Commit();
        }

        public void DeleteConnectionString(string name)
        {
            var settingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            settingsFile.RemoveConnectionString(name);
            settingsFile.Commit();
        }

        private class AppSettingsFile
        {
            [JsonIgnore]
            private readonly string _filePath;

            public AppSettingsFile() { }

            public AppSettingsFile(string filePath)
            {
                _filePath = filePath;
                try
                {
                    var content = FileSystemHelpers.ReadAllTextFromFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
                    var appSettings = JsonConvert.DeserializeObject<AppSettingsFile>(content);
                    IsEncrypted = appSettings.IsEncrypted;
                    Values = appSettings.Values;
                    ConnectionStrings = appSettings.ConnectionStrings;
                }
                catch
                {
                    Values = new Dictionary<string, string>();
                    ConnectionStrings = new Dictionary<string, string>();
                    IsEncrypted = true;
                }
            }

            public bool IsEncrypted { get; set; }
            public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> ConnectionStrings { get; set; } = new Dictionary<string, string>();

            public void SetSecret(string name, string value)
            {
                if (IsEncrypted)
                {
                    Values[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.Default.GetBytes(value), null, DataProtectionScope.CurrentUser));
                }
                else
                {
                    Values[name] = value;
                };
            }

            public void SetConnectionString(string name, string value)
            {
                if (IsEncrypted)
                {
                    ConnectionStrings[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.Default.GetBytes(value), null, DataProtectionScope.CurrentUser));
                }
                else
                {
                    ConnectionStrings[name] = value;
                };
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
                    return Values.ToDictionary(k => k.Key, v => string.IsNullOrEmpty(v.Value) ? string.Empty : Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), null, DataProtectionScope.CurrentUser)));
                }
                else
                {
                    return Values.ToDictionary(k => k.Key, v => v.Value);
                }
            }

            public IDictionary<string, string> GetConnectionStrings()
            {
                if (IsEncrypted)
                {
                    return ConnectionStrings.ToDictionary(k => k.Key, v => string.IsNullOrEmpty(v.Value) ? string.Empty : Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), null, DataProtectionScope.CurrentUser)));
                }
                else
                {
                    return ConnectionStrings.ToDictionary(k => k.Key, v => v.Value);
                }
            }
        }
    }
}
