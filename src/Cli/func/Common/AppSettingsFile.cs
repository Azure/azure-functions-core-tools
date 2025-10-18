// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    public class AppSettingsFile
    {
        private const string Reason = "secrets.manager.1";
        private readonly string _filePath;

        public AppSettingsFile(string filePath)
        {
            _filePath = filePath;
            try
            {
                var content = FileSystemHelpers.ReadAllTextFromFile(_filePath);
                var appSettings = JObject.Parse(content);

                var localSerializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                });

                IsEncrypted = appSettings.Value<bool?>("IsEncrypted") ?? true;
                var rawValues = appSettings["Values"]?.ToObject<Dictionary<string, string>>(localSerializer) ?? new Dictionary<string, string>();
                Values = EnvironmentHelper.NormalizeBooleanValues(rawValues);
                ConnectionStrings = appSettings["ConnectionStrings"]?.ToObject<Dictionary<string, JToken>>(localSerializer) ?? new Dictionary<string, JToken>();
                Host = appSettings["Host"]?.ToObject<HostStartSettings>(localSerializer) ?? new HostStartSettings();
            }
            catch (Exception ex)
            {
                if (ex is JsonException)
                {
                    ColoredConsole.WriteLine(WarningColor($"Failed to read app settings file at '{_filePath}'. Ensure it is a valid JSON file. {ex.Message}"));
                }

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
                Values[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.Default.GetBytes(value), Reason));
            }
            else
            {
                Values[name] = value;
            }
        }

        public void SetConnectionString(string name, string value, string providerName)
        {
            value = IsEncrypted
                ? Convert.ToBase64String(ProtectedData.Protect(Encoding.Default.GetBytes(value), Reason))
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
                    return Values.ToDictionary(k => k.Key, v => string.IsNullOrEmpty(v.Value) ? string.Empty : Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), Reason)));
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
                    ? Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Reason))
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
