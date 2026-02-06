// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ConfigurationProfiles;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class InitActionBundlesChannelTests
    {
        [Fact]
        public void ParseArgs_DefaultBundlesChannel_IsGA()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new InitAction(templatesManager, secretsManager, configProfiles);
            var args = Array.Empty<string>();

            action.ParseArgs(args);

            action.BundlesChannel.Should().Be(BundleChannel.GA);
        }

        [Fact]
        public void ParseArgs_WithBundlesChannelFlag_SetsChannelPreview()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new InitAction(templatesManager, secretsManager, configProfiles);
            var args = new[] { "--bundles-channel", "Preview" };

            action.ParseArgs(args);

            action.BundlesChannel.Should().Be(BundleChannel.Preview);
        }

        [Fact]
        public void ParseArgs_WithShortBundlesChannelFlag_SetsChannelExperimental()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new InitAction(templatesManager, secretsManager, configProfiles);
            var args = new[] { "-c", "Experimental" };

            action.ParseArgs(args);

            action.BundlesChannel.Should().Be(BundleChannel.Experimental);
        }

        [Fact]
        public void ParseArgs_WithBundlesChannelGA_SetsChannelGA()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new InitAction(templatesManager, secretsManager, configProfiles);
            var args = new[] { "--bundles-channel", "GA" };

            action.ParseArgs(args);

            action.BundlesChannel.Should().Be(BundleChannel.GA);
        }

        [Fact]
        public void ParseArgs_WithBothNoBundleAndBundlesChannel_ParsesBoth()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new InitAction(templatesManager, secretsManager, configProfiles);
            var args = new[] { "--no-bundle", "--bundles-channel", "Preview" };

            action.ParseArgs(args);

            action.ExtensionBundle.Should().BeFalse();
            action.BundlesChannel.Should().Be(BundleChannel.Preview);
        }
    }
}
