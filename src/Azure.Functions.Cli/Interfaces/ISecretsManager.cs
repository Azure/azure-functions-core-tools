using System.Collections.Generic;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface ISecretsManager
    {
        IDictionary<string, string> GetSecrets();
        IDictionary<string, string> GetConnectionStrings();
        void SetSecret(string name, string value);
        void SetConnectionString(string name, string value);
        void DecryptSettings();
        void EncryptSettings();
        void DeleteSecret(string name);
        void DeleteConnectionString(string name);
    }
}
