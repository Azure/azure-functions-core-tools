using Azure.Functions.Cli.Common;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace Azure.Functions.Cli.Common
{
    class AuthSettingsFile
    {
        public bool IsEncrypted { get; set; }
        public Dictionary<string, string> Values { get; set; }
        private readonly string _filePath;
        private const string reason = "secrets.manager.auth";

        public AuthSettingsFile(string filePath)
        {
            _filePath = filePath ?? throw new CliException("Received null value for auth settings filename.");
            
            string path = Path.Combine(Environment.CurrentDirectory, _filePath);

            if (File.Exists(path))
            {
                try
                {
                    var content = FileSystemHelpers.ReadAllTextFromFile(path);
                    var authSettings = JObject.Parse(content);
                    Values = new Dictionary<string, string>(authSettings.ToObject<Dictionary<string, string>>(), StringComparer.OrdinalIgnoreCase);
                }
                catch (UnauthorizedAccessException unauthorizedAccess)
                {
                    throw new CliException(unauthorizedAccess.Message);
                }
                catch (JsonReaderException jsonError)
                {
                    throw new CliException(jsonError.Message);
                }
                catch (Exception generic)
                {
                    throw new CliException(generic.ToString());
                }
            }
            else
            {
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                IsEncrypted = false;
            }
        }

        public void SetAuthSetting(string name, string value)
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

        public void RemoveSetting(string name)
        {
            if (Values.ContainsKey(name))
            {
                Values.Remove(name);
            }
        }

        public void Commit()
        {
            try
            {
                FileSystemHelpers.WriteAllTextToFile(_filePath, JsonConvert.SerializeObject(this.GetValues(), Formatting.Indented));
            }
            catch (UnauthorizedAccessException unauthorizedAccess)
            {
                throw new CliException(unauthorizedAccess.Message);
            }
            catch (Exception generic)
            {
                throw new CliException(generic.ToString());
            }
        }

        public IDictionary<string, string> GetValues()
        {
            if (IsEncrypted)
            {
                try
                {
                    return Values.ToDictionary(k => k.Key, v => string.IsNullOrEmpty(v.Value) ? string.Empty :
                        Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), reason)));
                }
                catch (Exception e)
                {
                    throw new CliException("Failed to decrypt settings.", e);
                }
            }
            else
            {
                return Values.ToDictionary(k => k.Key, v => v.Value);
            }
        }
    }
}