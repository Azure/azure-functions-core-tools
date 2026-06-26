// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Xunit;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public class PowerShellProjectInitializerTests
{
    [Fact]
    public void Stack_IsPowerShell()
    {
        Assert.Equal("powershell", new PowerShellProjectInitializer().Stack);
    }

    [Fact]
    public void SupportedLanguages_ContainsPowerShell()
    {
        Assert.Equal("PowerShell", Assert.Single(new PowerShellProjectInitializer().SupportedLanguages));
    }

    [Fact]
    public void GetInitOptions_ReturnsThreeOptions()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new PowerShellProjectInitializer().GetInitOptions(new InitOptionRegistry(root));

        Assert.Equal(3, options.Count);
    }

    [Fact]
    public void GetInitOptions_NullRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PowerShellProjectInitializer().GetInitOptions(null!));
    }

    [Fact]
    public async Task InitializeAsync_CreatesExpectedFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PowerShellProjectInitializer initializer = new();
            // Stub the Az module lookup to avoid network calls
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>("12");

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await initializer.InitializeAsync(context, root.Parse([]));

            Assert.True(File.Exists(Path.Combine(tempDir, "host.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "local.settings.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "profile.ps1")));
            Assert.True(File.Exists(Path.Combine(tempDir, ".gitignore")));
            Assert.False(File.Exists(Path.Combine(tempDir, "requirements.psd1")));

            string localSettings = File.ReadAllText(Path.Combine(tempDir, "local.settings.json"));
            Assert.Contains("powershell", localSettings);
            Assert.Contains("7.4", localSettings);
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
            PowerShellProjectInitializer initializer = new();
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>("12");

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await initializer.InitializeAsync(context, root.Parse(["--managed-dependencies"]));

            Assert.True(File.Exists(Path.Combine(tempDir, "requirements.psd1")));
            string requirements = File.ReadAllText(Path.Combine(tempDir, "requirements.psd1"));
            Assert.Contains("12", requirements);
            Assert.DoesNotContain("MAJOR_VERSION", requirements);

            // host.json should contain managedDependency
            string hostJson = File.ReadAllText(Path.Combine(tempDir, "host.json"));
            JsonObject host = JsonNode.Parse(hostJson)!.AsObject();
            Assert.True(host.ContainsKey("managedDependency"));
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
            PowerShellProjectInitializer initializer = new();
            initializer.GetLatestAzModuleMajorVersion = _ => Task.FromResult<string?>(null);

            RootCommand root = [];
            initializer.GetInitOptions(new InitOptionRegistry(root));

            InitContext context = new(
                WorkingDirectory.FromExplicit(tempDir),
                ProjectName: "my-ps-app",
                Language: null,
                Force: false);

            await initializer.InitializeAsync(context, root.Parse(["--no-bundles"]));

            string hostJson = File.ReadAllText(Path.Combine(tempDir, "host.json"));
            JsonObject host = JsonNode.Parse(hostJson)!.AsObject();
            Assert.False(host.ContainsKey("extensionBundle"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_NullContext_Throws()
    {
        PowerShellProjectInitializer initializer = new();
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => initializer.InitializeAsync(null!, root.Parse([])));
    }

    [Fact]
    public async Task InitializeAsync_NullParseResult_Throws()
    {
        PowerShellProjectInitializer initializer = new();
        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));

        InitContext context = new(
            WorkingDirectory.FromExplicit(Path.GetTempPath()),
            ProjectName: "my-ps-app",
            Language: null,
            Force: false);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => initializer.InitializeAsync(context, null!));
    }
}

