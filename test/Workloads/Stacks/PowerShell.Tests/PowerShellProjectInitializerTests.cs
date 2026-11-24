// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public class PowerShellProjectInitializerTests
{
    [Fact]
    public void Constructor_NullStack_Throws()
    {
        FluentActions.Invoking(() => new PowerShellProjectInitializer(null!))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("stack");
    }

    [Fact]
    public void Stack_IsPowerShell()
    {
        new PowerShellProjectInitializer(PowerShellWorkloadTests.Stack).Stack.Descriptor.StackIdentifier.Should().Be("powershell");
    }

    [Fact]
    public void SupportedLanguages_ContainsPowerShell()
    {
        new PowerShellProjectInitializer(PowerShellWorkloadTests.Stack).Stack.SupportedLanguages.Select(l => l.DisplayName).Should().ContainSingle().Subject.Should().Be("PowerShell");
    }

    [Fact]
    public void GetInitOptions_ReturnsFourOptions()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new PowerShellProjectInitializer(PowerShellWorkloadTests.Stack).GetInitOptions(new InitOptionRegistry(root));

        options.Count.Should().Be(4);
        options.Should().OnlyContain(option =>
            option.Description != null &&
            !string.IsNullOrWhiteSpace(option.Description) &&
            !option.Description.Contains('\r') &&
            !option.Description.Contains('\n'));
    }

    [Fact]
    public void GetInitOptions_NullRegistry_Throws()
    {
        FluentActions.Invoking(() => new PowerShellProjectInitializer(PowerShellWorkloadTests.Stack).GetInitOptions(null!))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task InitializeAsync_CreatesExpectedFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PowerShellProjectInitializer initializer = new(PowerShellWorkloadTests.Stack);
            // Stub the Az module lookup to avoid network calls
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>("12");

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await initializer.InitializeAsync(context, root.Parse([]), TestContext.Current.CancellationToken);

            File.Exists(Path.Combine(tempDir, "host.json")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "local.settings.json")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "profile.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, ".gitignore")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "requirements.psd1")).Should().BeFalse();

            string localSettings = File.ReadAllText(Path.Combine(tempDir, "local.settings.json"));
            localSettings.Should().Contain("powershell");
            localSettings.Should().Contain("7.4");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithManagedDependencies_WritesRequirementsPsd1()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PowerShellProjectInitializer initializer = new(PowerShellWorkloadTests.Stack);
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>("12");

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await initializer.InitializeAsync(
                context,
                root.Parse(["--managed-dependencies"]),
                TestContext.Current.CancellationToken);

            File.Exists(Path.Combine(tempDir, "requirements.psd1")).Should().BeTrue();
            string requirements = File.ReadAllText(Path.Combine(tempDir, "requirements.psd1"));
            requirements.Should().Contain("12");
            requirements.Should().NotContain("MAJOR_VERSION");

            // host.json should contain managedDependency
            string hostJson = File.ReadAllText(Path.Combine(tempDir, "host.json"));
            JsonObject host = JsonNode.Parse(hostJson)!.AsObject();
            host.ContainsKey("managedDependency").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithNoBundles_SkipsExtensionBundle()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PowerShellProjectInitializer initializer = new(PowerShellWorkloadTests.Stack);
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>(null);

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await initializer.InitializeAsync(
                context,
                root.Parse(["--no-bundles"]),
                TestContext.Current.CancellationToken);

            string hostJson = File.ReadAllText(Path.Combine(tempDir, "host.json"));
            JsonObject host = JsonNode.Parse(hostJson)!.AsObject();
            host.ContainsKey("extensionBundle").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_NullContext_Throws()
    {
        PowerShellProjectInitializer initializer = new(PowerShellWorkloadTests.Stack);
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));

        await FluentActions.Awaiting(() => initializer.InitializeAsync(null!, root.Parse([]))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InitializeAsync_WithRuntimeVersion_WritesCorrectVersion()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PowerShellProjectInitializer initializer = new(PowerShellWorkloadTests.Stack);
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>(null);

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await initializer.InitializeAsync(
                context,
                root.Parse(["--runtime-version", "7.6"]),
                TestContext.Current.CancellationToken);

            string localSettings = File.ReadAllText(Path.Combine(tempDir, "local.settings.json"));
            localSettings.Should().Contain("\"7.6\"");
            localSettings.Should().NotContain("7.4");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithUnsupportedRuntimeVersion_Throws()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PowerShellProjectInitializer initializer = new(PowerShellWorkloadTests.Stack);
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>(null);

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await FluentActions.Awaiting(() => initializer.InitializeAsync(context, root.Parse(["--runtime-version", "6.0"]))).Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_NullParseResult_Throws()
    {
        PowerShellProjectInitializer initializer = new(PowerShellWorkloadTests.Stack);
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));

        InitContext context = new(
            WorkingDirectory.FromExplicit(Path.GetTempPath()),
            ProjectName: "my-ps-app",
            Language: null,
            Force: false);

        await FluentActions.Awaiting(() => initializer.InitializeAsync(context, null!)).Should().ThrowAsync<ArgumentNullException>();
    }
}
