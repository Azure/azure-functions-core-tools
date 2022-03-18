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
        // Attempted Key Vault references are seen as those matching the regular expression
        // "^@Microsoft.KeyVault(\S*)$".
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
        [InlineData("SecretUri=https://sampleURL/secrets/mysecret/version", true)]
        [InlineData("VaultName=sampleVault;SecretName=mysecret", false)]
        [InlineData("VaultName=sampleVault;SecretName=mysecret;SecretVersion=secretVersion", false)]
        // The below syntax is not yet supported in Core Tools
        [InlineData("SecretUri=https://sampleURL/secrets/mysecret/", false)]
        public void PrimaryReferenceStringRegexMatchesAppropriately(string value, bool shouldMatch)
        {
            var match = _keyVaultReferencesManager.ParsePrimaryRegexSecret(value) != null;
            Assert.True(!(match ^ shouldMatch));
        }

        [Theory]
        [InlineData("SecretUri=https://sampleURL/secrets/mysecret/", false)]
        [InlineData("SecretUri=https://sampleURL/secrets/mysecret/version", false)]
        [InlineData("VaultName=sampleVault;SecretName=mysecret", true)]
        // The below syntax is not yet supported in Core Tools
        [InlineData("VaultName=sampleVault;SecretName=mysecret;SecretVersion=secretVersion", false)]
        public void SecondaryReferenceStringRegexMatchesAppropriately(string value, bool shouldMatch)
        {
            var match = _keyVaultReferencesManager.ParseSecondaryRegexSecret(value) != null;
            Assert.True(!(match ^ shouldMatch));
        }
    }
}