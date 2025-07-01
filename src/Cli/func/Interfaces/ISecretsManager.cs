// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Interfaces
{
    public interface ISecretsManager
    {
        internal IDictionary<string, string> GetSecrets();

        internal IEnumerable<ConnectionString> GetConnectionStrings();

        internal void SetSecret(string name, string value);

        internal void SetConnectionString(string name, string value);

        internal void DecryptSettings();

        internal void EncryptSettings();

        internal void DeleteSecret(string name);

        internal void DeleteConnectionString(string name);

        internal HostStartSettings GetHostStartSettings();
    }
}
