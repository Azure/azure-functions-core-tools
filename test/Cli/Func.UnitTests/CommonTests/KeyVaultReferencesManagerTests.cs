// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Common;
using Colors.Net;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.CommonTests
{
    public class KeyVaultReferencesManagerTests
    {
        private readonly KeyVaultReferencesManager _keyVaultReferencesManager = new KeyVaultReferencesManager();
        private IDictionary<string, string> _settings;

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
        public void ParseSecretEmitsWarningWithUnsuccessfullyMatchedKeyVaultReferences(string key, string value, bool attemptedKeyVaultReference)
        {
            // Arrange
            var output = new StringBuilder();
            var console = Substitute.For<IConsoleWriter>();
            console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => output.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;
            ColoredConsole.Error = console;

            // Act
            _keyVaultReferencesManager.ParseSecret(key, value);

            // Assert
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
            // Act
            var matchResult = _keyVaultReferencesManager.ParseVaultReference(vaultReference);

            // Assert
            Assert.True(!((matchResult != null) ^ shouldMatch));
            if (shouldMatch)
            {
                Assert.Equal(expectedVaultUri, matchResult.Uri.ToString());
                Assert.Equal(expectedSecretName, matchResult.Name);
                Assert.Equal(expectedVersion, matchResult.Version);
            }
        }

        [Theory]
        [InlineData("SecretUri", "SecretUri=https://example.vault.azure.net/secrets/mysecret", "https://example.vault.azure.net/secrets/mysecret")]
        [InlineData("VaultName", "VaultName=myVault;SecretName=mySecret", "myVault")]
        [InlineData("SecretName", "VaultName=myVault;SecretName=mySecret", "mySecret")]
        [InlineData("SecretVersion", "VaultName=myVault;SecretName=mySecret;SecretVersion=v1", "v1")]
        [InlineData("SecretUri", "VaultName=myVault;SecretName=mySecret", null)]
        [InlineData("NonExistent", "VaultName=myVault;SecretName=mySecret", null)]
        public void GetValueFromVaultReferenceExtractsCorrectValue(string key, string vaultReference, string expectedValue)
        {
            // Act
            var result = _keyVaultReferencesManager.GetValueFromVaultReference(key, vaultReference);

            // Assert
            Assert.Equal(expectedValue, result);
        }
    }
}
