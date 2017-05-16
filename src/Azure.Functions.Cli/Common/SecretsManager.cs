using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Common
{
    internal class SecretsManager : ISecretsManager
    {
        private static bool warningPrinted = false;

        public static string AppSettingsFilePath
        {
            get
            {
                var rootPath = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
                var newFilePath = Path.Combine(rootPath, "local.settings.json");
                var oldFilePath = Path.Combine(rootPath, "appsettings.json");
                if (!FileSystemHelpers.FileExists(oldFilePath))
                {
                    return newFilePath;
                }
                else if (FileSystemHelpers.FileExists(oldFilePath) && FileSystemHelpers.FileExists(newFilePath))
                {
                    if (!warningPrinted)
                    {
                        ColoredConsole.WriteLine(WarningColor("Warning: found both 'local.settings.json' and 'appsettings.json'. Ignoring 'appsettings.json'."));
                        warningPrinted = true;
                    }
                    return newFilePath;
                }
                else if (FileSystemHelpers.FileExists(oldFilePath))
                {
                    if (!warningPrinted)
                    {
                        ColoredConsole.WriteLine(WarningColor($"Warning: The filename 'appsettings.json' is deprecated. Rename it to local.settings.json"));
                        warningPrinted = true;
                    }
                    return oldFilePath;
                }
                else
                {
                    return newFilePath;
                }
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

        public IDictionary<string, string> GetConnectionStrings()
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
            appSettingsFile.SetConnectionString(name, value);
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

                foreach(var pair in connectionStrings)
                {
                    settingsFile.SetConnectionString(pair.Key, pair.Value);
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

                foreach(var pair in connectionStrings)
                {
                    settingsFile.SetConnectionString(pair.Key, pair.Value);
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
                    ConnectionStrings = new Dictionary<string, string>();
                    IsEncrypted = true;
                }
            }

            public bool IsEncrypted { get; set; }
            public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> ConnectionStrings { get; set; } = new Dictionary<string, string>();

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public HostStartSettings Host { get; set; }

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
                    try
                    {
                        return Values.ToDictionary(k => k.Key, v => string.IsNullOrEmpty(v.Value) ? string.Empty : Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), null, DataProtectionScope.CurrentUser)));
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

            public IDictionary<string, string> GetConnectionStrings()
            {
                if (IsEncrypted)
                {
                    try
                    {
                        return ConnectionStrings.ToDictionary(k => k.Key, v => string.IsNullOrEmpty(v.Value) ? string.Empty : Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), null, DataProtectionScope.CurrentUser)));
                    }
                    catch (Exception e)
                    {
                        throw new CliException("Failed to decrypt settings. Encrypted settings only be edited through 'func settings add'.", e);
                    }
                }
                else
                {
                    return ConnectionStrings.ToDictionary(k => k.Key, v => v.Value);
                }
            }
        }
    }
}
