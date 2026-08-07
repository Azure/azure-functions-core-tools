// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;
using Xunit;
using Constants = Azure.Functions.Cli.Abstractions.Common.Constants;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

[Collection("FuncHomeEnvVarTests")]
public class FuncTemplateEngineHostFactoryTests
{
    [Fact]
    public void SettingsLocation_WithConfiguredFuncHome_UsesFuncOwnedTemplatesDirectory()
    {
        string funcHome = Path.Combine(Path.GetTempPath(), "func-template-host-" + Guid.NewGuid().ToString("N"));

        string settingsLocation = WithFuncHome(funcHome, () => CreateFactory().SettingsLocation);

        settingsLocation.Should().Be(Path.GetFullPath(Path.Combine(
            funcHome,
            FuncTemplateEngineHostFactory.SettingsDirectoryName)));
    }

    [Fact]
    public void SettingsLocation_IsNotDotnetCliHive()
    {
        string dotnetCliHive = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".templateengine",
            "dotnetcli");

        string settingsLocation = WithFuncHome(null, () => CreateFactory().SettingsLocation);

        settingsLocation.Should().NotStartWith(dotnetCliHive);
    }

    [Fact]
    public void CreateHost_ReturnsFreshFuncHosts()
    {
        FuncTemplateEngineHostFactory factory = CreateFactory();

        using ITemplateEngineHost first = factory.CreateHost();
        using ITemplateEngineHost second = factory.CreateHost();

        first.HostIdentifier.Should().Be(FuncTemplateEngineHost.Identifier);
        second.HostIdentifier.Should().Be(FuncTemplateEngineHost.Identifier);
        second.Should().NotBeSameAs(first);
    }

    private static FuncTemplateEngineHostFactory CreateFactory()
    {
        ICliVersionProvider versionProvider = Substitute.For<ICliVersionProvider>();
        versionProvider.Version.Returns("5.0.0");
        return new FuncTemplateEngineHostFactory(versionProvider);
    }

    private static T WithFuncHome<T>(string? value, Func<T> action)
    {
        string? previous = Environment.GetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable, value);
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable, previous);
        }
    }
}
