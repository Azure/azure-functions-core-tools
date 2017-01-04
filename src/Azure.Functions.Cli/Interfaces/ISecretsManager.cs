using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface ISecretsManager
    {
        IDictionary<string, string> GetSecrets();
        void SetSecret(string name, string value);
        void DecryptSettings();
        void EncryptSettings();
        void DeleteSecret(string Name);
    }
}
