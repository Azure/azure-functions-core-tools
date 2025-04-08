using System.Collections.Generic;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Interfaces
{
    public interface ISecretsManager
    {
        IDictionary<string, string> GetSecrets();
        IEnumerable<ConnectionString> GetConnectionStrings();
        void SetSecret(string name, string value);
        void SetConnectionString(string name, string value);
        void DecryptSettings();
        void EncryptSettings();
        void DeleteSecret(string name);
        void DeleteConnectionString(string name);
        HostStartSettings GetHostStartSettings();
    }
}
