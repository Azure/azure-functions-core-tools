using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Azure.Functions.Cli.Common
{
    class KeyVaultReferencesManager
    {
        private readonly ConcurrentDictionary<string, SecretClient> clients = new ConcurrentDictionary<string, SecretClient>();
        private readonly TokenCredential credential = new AzureCliCredential();

        public void ResolveKeyVaultReferences(IDictionary<string, string> settings)
        {
            foreach (var key in settings.Keys.ToList())
            {
                var keyVaultValue = GetSecretValue(settings[key]);
                if (keyVaultValue != null)
                {
                    settings[key] = keyVaultValue;
                }
            }
        }

        public string GetSecretValue(string secretIdentifier)
        {
            var result = ParseSecret(secretIdentifier);

            if (result.IsSecret)
            {
                var client = GetSecretClient(result.Uri);
                var secret = client.GetSecret(result.Name, result.Version);
                return secret.Value.Value;
            }

            return null;
        }

        private ParseSecretResult ParseSecret(string secretIdentifier)
        {
            var uriMatches = Regex.Match(secretIdentifier, @"^@Microsoft.KeyVault\(SecretUri=(https://.+?)/secrets/([^/]+)/?(.*)\)$");
            if (uriMatches.Success)
            {
                return new ParseSecretResult
                {
                    IsSecret = true,
                    Uri = new Uri(uriMatches.Groups[1].Value),
                    Name = uriMatches.Groups[2].Value,
                    Version = uriMatches.Groups[3].Value
                };
            }

            return new ParseSecretResult();
        }

        private SecretClient GetSecretClient(Uri vaultUri)
        {
            return clients.GetOrAdd(vaultUri.ToString(), _ => new SecretClient(vaultUri, credential));
        }

        private class ParseSecretResult
        {
            public bool IsSecret { get; set; }
            public Uri Uri { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
        }
    }
}