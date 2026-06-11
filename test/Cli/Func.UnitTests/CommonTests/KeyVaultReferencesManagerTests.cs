// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using AwesomeAssertions;
using Azure.Functions.Cli.Common;
using Colors.Net;
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

        [Fact]
        public void ResolveKeyVaultReferencesEmitsWarningWhenParsedReferenceFailsToResolve()
        {
            // Arrange
            const string failedKey = "FailedSecret";
            const string failedReference = "@Microsoft.KeyVault(VaultName=testvault;SecretName=throwsecret)";
            const string resolvedKey = "ResolvedSecret";
            const string resolvedValue = "resolved-value";
            var manager = new TestKeyVaultReferencesManager(secretName =>
            {
                if (secretName == "throwsecret")
                {
                    throw new InvalidOperationException("Could not resolve secret");
                }

                return resolvedValue;
            });
            _settings = new Dictionary<string, string>
            {
                { failedKey, failedReference },
                { resolvedKey, "@Microsoft.KeyVault(VaultName=testvault;SecretName=resolvedsecret)" },
            };

            // Act
            var outputString = CaptureConsoleOutput(() => manager.ResolveKeyVaultReferences(_settings));

            // Assert
            outputString.Should().Contain($"Unable to resolve the Key Vault reference for setting: {failedKey}");
            outputString.Should().NotContain(failedReference);
            outputString.Should().NotContain("throwsecret");
            outputString.Should().NotContain(resolvedValue);
            _settings[failedKey].Should().Be(failedReference);
            _settings[resolvedKey].Should().Be(resolvedValue);
        }

        [Fact]
        public void ResolveKeyVaultReferencesDoesNotEmitResolutionWarningForUnparseableReferences()
        {
            // Arrange
            const string key = "UnparseableSecret";
            const string reference = "@Microsoft.KeyVault(VaultName=testvault;)";
            var manager = new TestKeyVaultReferencesManager(_ => throw new InvalidOperationException("Should not resolve"));
            _settings = new Dictionary<string, string>
            {
                { key, reference },
            };

            // Act
            var outputString = CaptureConsoleOutput(() => manager.ResolveKeyVaultReferences(_settings));

            // Assert
            outputString.Should().Contain($"Unable to parse the Key Vault reference for setting: {key}");
            outputString.Should().NotContain($"Unable to resolve the Key Vault reference for setting: {key}");
            outputString.Should().NotContain(reference);
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
            // Act
            var outputString = CaptureConsoleOutput(() => _keyVaultReferencesManager.ParseSecret(key, value));

            // Assert
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

        private static string CaptureConsoleOutput(Action action)
        {
            var output = new StringBuilder();
            var console = Substitute.For<IConsoleWriter>();
            console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => output.Append(o?.ToString()))).Returns(console);

            var oldOut = ColoredConsole.Out;
            var oldErr = ColoredConsole.Error;
            try
            {
                ColoredConsole.Out = console;
                ColoredConsole.Error = console;
                action();
                return output.ToString();
            }
            finally
            {
                ColoredConsole.Out = oldOut;
                ColoredConsole.Error = oldErr;
            }
        }

        private class TestKeyVaultReferencesManager : KeyVaultReferencesManager
        {
            private readonly Func<string, string> _getSecretValue;

            public TestKeyVaultReferencesManager(Func<string, string> getSecretValue)
            {
                _getSecretValue = getSecretValue;
            }

            protected override string GetSecretValue(ParseSecretResult parseResult)
            {
                return _getSecretValue(parseResult.Name);
            }
        }
    }
}
