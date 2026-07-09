// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using Xunit;
using Constants = Azure.Functions.Cli.Abstractions.Common.Constants;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

// Shares the FuncHome env-var collection so mutating FUNC_CLI_HOME never races
// other tests that read the func home.
[Collection("FuncHomeEnvVarTests")]
public class FuncTemplateEngineHostFactoryTests
{
    [Fact]
    public void SettingsLocation_WithEnvHome_IsRootedUnderTempHome()
    {
        string tempHome = Path.Combine(Path.GetTempPath(), "func-te-" + Guid.NewGuid().ToString("N"));

        string settingsLocation = WithFuncHome(tempHome, () => CreateFactory().SettingsLocation);

        string expected = Path.GetFullPath(
            Path.Combine(tempHome, FuncTemplateEngineHostFactory.SettingsDirectoryName));
        Assert.Equal(expected, settingsLocation);
    }

    [Fact]
    public void SettingsLocation_WithoutEnvHome_DefaultsUnderUserProfileFuncHome()
    {
        string expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName,
            FuncTemplateEngineHostFactory.SettingsDirectoryName));

        string settingsLocation = WithFuncHome(null, () => CreateFactory().SettingsLocation);

        Assert.Equal(expected, settingsLocation);
    }

    [Fact]
    public void SettingsLocation_IsNotDotnetCliHive()
    {
        string dotnetCliHive = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".templateengine",
            "dotnetcli");

        string settingsLocation = WithFuncHome(null, () => CreateFactory().SettingsLocation);

        Assert.False(
            settingsLocation.StartsWith(dotnetCliHive, StringComparison.OrdinalIgnoreCase),
            $"Settings location '{settingsLocation}' must not live under the dotnet CLI hive '{dotnetCliHive}'.");
    }

    [Fact]
    public void CreateHost_UsesFuncHostIdentifier()
    {
        using var host = CreateFactory().CreateHost();

        Assert.Equal(FuncTemplateEngineHostFactory.HostIdentifier, host.HostIdentifier);
        Assert.Equal("func", host.HostIdentifier);
    }

    [Fact]
    public void CreateHost_ReturnsFreshInstances()
    {
        var factory = CreateFactory();

        using var first = factory.CreateHost();
        using var second = factory.CreateHost();

        Assert.NotSame(first, second);
    }

    private static FuncTemplateEngineHostFactory CreateFactory()
    {
        var versionProvider = Substitute.For<ICliVersionProvider>();
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
