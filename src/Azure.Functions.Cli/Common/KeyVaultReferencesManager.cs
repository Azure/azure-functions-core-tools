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
        private const string directiveStart = "@Microsoft.KeyVault(";
        private const string directiveEnd = ")";
        private const string vaultUriSuffix = "vault.azure.net";
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
            var referenceString = ExtractReferenceString(value);
            if (string.IsNullOrEmpty(referenceString))
            {
                return null;
            }

            try
            {
                var uriMatches = Regex.Match(value, @"SecretUri=(https://.+?)/secrets/([^/]+)/?(.*)");
                if (uriMatches.Success)
                {
                    return new ParseSecretResult
                    {
                        Uri = new Uri(uriMatches.Groups[1].Value),
                        Name = uriMatches.Groups[2].Value,
                        Version = uriMatches.Groups[3].Value
                    };
                }

                var keyValuePairs = referenceString.Split(";")
                                                .Select(item => item.Split("="))
                                                .ToDictionary(pair => pair[0], pair => pair[1]);

                return new ParseSecretResult
                {
                    Uri = new Uri($"https://{keyValuePairs["VaultName"]}.{vaultUriSuffix}"),
                    Name = keyValuePairs["SecretName"],
                    Version = keyValuePairs["SecretVersion"]
                };
            }
            catch
            {
                throw new FormatException($"Key Vault Reference format invalid: {value}");
            }
        }

        private string ExtractReferenceString(string value)
        {
            if (value == null || 
                !(value.StartsWith(directiveStart) && value.EndsWith(directiveEnd)))
            {
                return null;
            }

            return value.Substring(directiveStart.Length, value.Length - directiveStart.Length - directiveEnd.Length);
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