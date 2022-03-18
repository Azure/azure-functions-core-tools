using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Colors.Net;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    class KeyVaultReferencesManager
    {
        private const string vaultUriSuffix = "vault.azure.net";
        private static readonly Regex BasicKeyVaultReferenceRegex = new Regex(@"^@Microsoft.KeyVault\((?<ReferenceString>\S*)\)$", RegexOptions.Compiled);
        private static readonly Regex PrimaryReferenceStringRegex = new Regex(@"^SecretUri=(?<VaultUri>https://[^\s/]+)/secrets/(?<SecretName>[^\s/]+)/(?<Version>[^\s/]+)$", RegexOptions.Compiled);
        private static readonly Regex SecondaryReferenceStringRegex = new Regex(@"^VaultName=(?<VaultName>[^\s;]+);SecretName=(?<SecretName>[^\s;]+)$", RegexOptions.Compiled); 
        private readonly ConcurrentDictionary<string, SecretClient> clients = new ConcurrentDictionary<string, SecretClient>();
        private readonly TokenCredential credential = new DefaultAzureCredential();

        public void ResolveKeyVaultReferences(IDictionary<string, string> settings)
        {
            foreach (var key in settings.Keys.ToList())
            {
                try
                {
                    var keyVaultValue = GetSecretValue(key, settings[key]);
                    if (keyVaultValue != null)
                    {
                        settings[key] = keyVaultValue;
                    }
                }
                catch
                {
                    // Do not block StartHostAction if secret cannot be resolved: instead, skip it
                    // and attempt to resolve other secrets
                    continue;
                }   
            }
        }

        private string GetSecretValue(string key, string value)
        {
            var result = ParseSecret(key, value);

            if (result != null)
            {
                var client = GetSecretClient(result.Uri);
                var secret = client.GetSecret(result.Name, result.Version);
                return secret.Value.Value;
            }

            return null;
        }

        internal ParseSecretResult ParseSecret(string key, string value)
        {
            // If the value is null, then we return nothing, as the subsequent call to
            // UpdateEnvironmentVariables(settings) will log to the user that the setting
            // is skipped. We check here, because Regex.Match throws when supplied with a
            // null value.
            if (value == null)
            {
                return null;
            }
            // Determine if the secret value is attempting to use a key vault reference
            var keyVaultReferenceMatch = BasicKeyVaultReferenceRegex.Match(value);
            if (keyVaultReferenceMatch.Success)
            {
                var referenceString = keyVaultReferenceMatch.Groups["ReferenceString"].Value;
                var result = ParsePrimaryRegexSecret(referenceString) ?? ParseSecondaryRegexSecret(referenceString);
                // If we detect that a key vault reference was attempted, but did not match any of
                // the supported formats, we write a warning to the console.
                if (result == null)
                {
                    ColoredConsole.WriteLine(WarningColor($"Unable to parse the Key Vault reference for setting: {key}"));
                }
                return result;
            }
            return null;
        }

        internal ParseSecretResult ParsePrimaryRegexSecret(string value)
        {
            var match = PrimaryReferenceStringRegex.Match(value);
            if (match.Success)
            {
                return new ParseSecretResult
                {
                    Uri = new Uri(match.Groups["VaultUri"].Value),
                    Name = match.Groups["SecretName"].Value,
                    Version = match.Groups["Version"].Value
                };
            }
            return null;
        }

        internal ParseSecretResult ParseSecondaryRegexSecret(string value)
        {
            var altMatch = SecondaryReferenceStringRegex.Match(value);
            if (altMatch.Success)
            {
                return new ParseSecretResult
                {
                    Uri = new Uri($"https://{altMatch.Groups["VaultName"]}.{vaultUriSuffix}"),
                    Name = altMatch.Groups["SecretName"].Value
                };
            }
            return null;
        }

        private SecretClient GetSecretClient(Uri vaultUri)
        {
            return clients.GetOrAdd(vaultUri.ToString(), _ => new SecretClient(vaultUri, credential));
        }

        internal class ParseSecretResult
        {
            public Uri Uri { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
        }
    }
}
