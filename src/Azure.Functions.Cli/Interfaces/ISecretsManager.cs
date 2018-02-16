﻿using System.Collections.Generic;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface ISecretsManager
    {
        IDictionary<string, string> GetSecrets();
        IEnumerable<ConnectionString> GetConnectionStrings();
        void SetSecret(string name, string value);
        void SetConnectionString(string name, string value, string ProviderName);
        void DecryptSettings();
        void EncryptSettings();
        void DeleteSecret(string name);
        void DeleteConnectionString(string name);
        HostStartSettings GetHostStartSettings();
    }
}
