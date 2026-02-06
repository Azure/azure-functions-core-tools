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
    public class CreateFunctionActionBundlesChannelTests
    {
        [Fact]
        public void ParseArgs_DefaultBundlesChannel_IsNull()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var contextHelpManager = Substitute.For<IContextHelpManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new CreateFunctionAction(templatesManager, secretsManager, contextHelpManager, configProfiles);
            var args = Array.Empty<string>();

            action.ParseArgs(args);

            action.BundlesChannel.Should().BeNull();
        }

        [Fact]
        public void ParseArgs_WithBundlesChannelFlag_SetsChannelPreview()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var contextHelpManager = Substitute.For<IContextHelpManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new CreateFunctionAction(templatesManager, secretsManager, contextHelpManager, configProfiles);
            var args = new[] { "--bundles-channel", "Preview" };

            action.ParseArgs(args);

            action.BundlesChannel.Should().Be(BundleChannel.Preview);
        }

        [Fact]
        public void ParseArgs_WithShortBundlesChannelFlag_SetsChannelExperimental()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var contextHelpManager = Substitute.For<IContextHelpManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new CreateFunctionAction(templatesManager, secretsManager, contextHelpManager, configProfiles);
            var args = new[] { "-c", "Experimental" };

            action.ParseArgs(args);

            action.BundlesChannel.Should().Be(BundleChannel.Experimental);
        }

        [Fact]
        public void ParseArgs_WithBundlesChannelGA_SetsChannelGA()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var contextHelpManager = Substitute.For<IContextHelpManager>();
            var configProfiles = new List<IConfigurationProfile>();
            var action = new CreateFunctionAction(templatesManager, secretsManager, contextHelpManager, configProfiles);
            var args = new[] { "--bundles-channel", "GA" };

            action.ParseArgs(args);

            action.BundlesChannel.Should().Be(BundleChannel.GA);
        }
    }
}
