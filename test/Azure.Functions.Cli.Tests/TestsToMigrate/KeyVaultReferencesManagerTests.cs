// Requires KeyVault resource or emulator

using System;
using System.Collections.Generic;
using System.Text;
using Colors.Net;
using FluentAssertions;
using NSubstitute;
using Xunit;

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests
{
    public class KeyVaultReferencesManagerTests
    {
        private IDictionary<string, string> _settings;

        private readonly KeyVaultReferencesManager _keyVaultReferencesManager = new KeyVaultReferencesManager();

        [Theory]
        [InlineData("", null)]
        [InlineData("testKey", null)]
        [InlineData("testKey", "")]
        [InlineData("testKey", "testValue")]
        [InlineData("testKey", "testValue: {\"nestedKey\": \"nestedValue\"}")]
        public void ResolveKeyVaultReferencesDoesNotThrow(string key, string value)
        {
            _settings = new Dictionary<string, string>
            {
                { Constants.AzureWebJobsStorage, "UseDevelopmentStorage=true" },
            };
            _settings.Add(key, value);
            Exception exception = null;
            try
            {
                _keyVaultReferencesManager.ResolveKeyVaultReferences(_settings);
            }
            catch (Exception e)
            {
                exception = e;
            }
            exception.Should().BeNull();
        }

        [Theory]
        [InlineData("test", null, false)]
        [InlineData("test", "@Microsoft.KeyVault()", true)]
        [InlineData("test", "@Microsoft.KeyVault(string)", true)]
        [InlineData("test", "@Microsoft.KeyVault(SecretUri=bad uri)", true)]
        [InlineData("test", "@Microsoft.KeyVault(VaultName=vault;)", true)] // missing secret name
        [InlineData("test", "@Microsoft.KeyVault(SecretName=vault;)", true)] // missing vault name
        [InlineData("test", "@Microsoft-KeyVault()", false)] // hyphen instead of dot
        // Attempted Key Vault references are seen as those matching the regular expression
        // "^@Microsoft.KeyVault(.*)$".
        public void ParseSecretEmitsWarningWithUnsuccessullyMatchedKeyVaultReferences(string key, string value, bool attemptedKeyVaultReference)
        {
            var output = new StringBuilder();
            var console = Substitute.For<IConsoleWriter>();
            console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => output.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;
            ColoredConsole.Error = console;

            _keyVaultReferencesManager.ParseSecret(key, value);
            var outputString = output.ToString();
            if (attemptedKeyVaultReference)
            {
                outputString.Should().Contain($"Unable to parse the Key Vault reference for setting: {key}");
                outputString.Should().NotContain(value);
            }
            else
            {
                outputString.Should().BeEmpty();
            }
        }

        // See https://docs.microsoft.com/en-us/azure/app-service/app-service-key-vault-references
        // for more detail on supported key vault reference syntax.
        [Theory]
        [InlineData("SecretUri=https://sampleurl/secrets/mysecret/version", true, "https://sampleurl/", "mysecret", "version")]
        [InlineData("SecretUri=https://sampleurl/secrets/mysecret/version;", true, "https://sampleurl/", "mysecret", "version")] // with semicolon at the end
        [InlineData("SecretUri=https://sampleurl/secrets/mysecret/", true, "https://sampleurl/", "mysecret", null)]
        [InlineData("VaultName=sampleVault;SecretName=mysecret", true, "https://samplevault.vault.azure.net/", "mysecret", null)]
        [InlineData("VaultName=sampleVault;SecretName=mysecret;", true, "https://samplevault.vault.azure.net/", "mysecret", null)] // with semicolon at the end
        [InlineData("VaultName=sampleVault;SecretName=mysecret;SecretVersion=secretVersion", true, "https://samplevault.vault.azure.net/", "mysecret", "secretVersion")]
        [InlineData("SecretName=mysecret;VaultName=sampleVault;SecretVersion=secretVersion", true, "https://samplevault.vault.azure.net/", "mysecret", "secretVersion")] // different order
        public void ParseVaultReferenceMatchesFieldsAppropriately(
            string vaultReference,
            bool shouldMatch,
            string expectedVaultUri = null,
            string expectedSecretName = null,
            string expectedVersion = null)
        {
            var matchResult = _keyVaultReferencesManager.ParseVaultReference(vaultReference);

            Assert.True(!((matchResult != null) ^ shouldMatch));
            if (shouldMatch)
            {
                Assert.Equal(matchResult.Uri.ToString(), expectedVaultUri);
                Assert.Equal(matchResult.Name, expectedSecretName);
                Assert.Equal(matchResult.Version, expectedVersion);
            }
        }
    }
}