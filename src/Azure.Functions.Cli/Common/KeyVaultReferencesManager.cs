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
        private static readonly Regex PrimaryKeyVaultReferenceRegex = new Regex(@"^@Microsoft.KeyVault\(SecretUri=(?<VaultUri>[\S^/]+)/(?<Secrets>[\S^/]+)/(?<SecretName>[\S^/]+)/(?<Version>[\S^/]+)\)$", RegexOptions.Compiled);
        private static readonly Regex SecondaryKeyVaultReferenceRegex = new Regex(@"^@Microsoft.KeyVault\(VaultName=(?<VaultName>[\S^;]+);SecretName=(?<SecretName>[\S^;]+)\)$", RegexOptions.Compiled); 
        private readonly ConcurrentDictionary<string, SecretClient> clients = new ConcurrentDictionary<string, SecretClient>();
        private readonly TokenCredential credential = new DefaultAzureCredential();

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

        private string GetSecretValue(string value)
        {
            var result = ParseSecret(value);

            if (result != null)
            {
                var client = GetSecretClient(result.Uri);
                var secret = client.GetSecret(result.Name, result.Version);
                return secret.Value.Value;
            }

            return null;
        }

        private ParseSecretResult ParseSecret(string value)
        {
            try
            {
                var match = PrimaryKeyVaultReferenceRegex.Match(value);
                if (match.Success)
                {
                    return new ParseSecretResult
                    {
                        Uri = new Uri(match.Groups["VaultUri"].Value),
                        Name = match.Groups["SecretName"].Value,
                        Version = match.Groups["Version"].Value
                    };
                }
                var altMatch = SecondaryKeyVaultReferenceRegex.Match(value);
                if (altMatch.Success)
                {
                    return new ParseSecretResult
                    {
                        Uri = new Uri(altMatch.Groups["VaultUri"].Value),
                        Name = altMatch.Groups["SecretName"].Value,
                        Version = altMatch.Groups["Version"].Value
                    };
                }
            }
            catch
            {
                throw new FormatException($"Key Vault Reference format invalid: {value}");
            }
            return null;
        }

        private SecretClient GetSecretClient(Uri vaultUri)
        {
            return clients.GetOrAdd(vaultUri.ToString(), _ => new SecretClient(vaultUri, credential));
        }

        private class ParseSecretResult
        {
            public Uri Uri { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
        }
    }
}
