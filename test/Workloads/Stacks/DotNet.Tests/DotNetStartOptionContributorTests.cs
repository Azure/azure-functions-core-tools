// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetStartOptionContributorTests
{
    private static (DotNetStartOptionContributor Contributor, RootCommand Root) CreateContributor()
    {
        var contributor = new DotNetStartOptionContributor();
        RootCommand root = [];
        contributor.GetStartOptions(new StartOptionRegistry(root));
        return (contributor, root);
    }

    [Fact]
    public void Stack_IsDotNet()
    {
        new DotNetStartOptionContributor().Stack.Should().Be("dotnet");
    }

    [Fact]
    public void DeferredWorkerEnvironmentPrefix_MatchesSharedConstant()
    {
        // The host Program.cs duplicates this value because it can't reference Abstractions.
        // This test ensures the contributor's constant (derived from StartHostConfiguration) stays
        // in sync with the expected literal the host uses.
        DotNetStartOptionContributor.DeferredWorkerEnvironmentPrefix
            .Should().Be(StartHostConfiguration.DeferredWorkerEnvironmentPrefix);
    }

    [Fact]
    public void GetStartOptions_RegistersExpectedOptions()
    {
        (DotNetStartOptionContributor contributor, RootCommand root) = CreateContributor();

        root.Options.Select(o => o.Name).Should().Contain(
            ["--dotnet-isolated-debug", "--enable-json-output", "--json-output-file"]);
        contributor.GetStartOptions(new StartOptionRegistry(root)).Should().HaveCount(3);
    }

    [Fact]
    public void Configure_WithNoOptions_ReturnsEmpty()
    {
        (DotNetStartOptionContributor contributor, RootCommand root) = CreateContributor();
        ParseResult parseResult = root.Parse(string.Empty);

        contributor.Configure(parseResult).Should().BeSameAs(StartHostConfiguration.Empty);
    }

    [Fact]
    public void Configure_WithDebug_SetsDebuggerWaitAndStartupHook()
    {
        (DotNetStartOptionContributor contributor, RootCommand root) = CreateContributor();
        ParseResult parseResult = root.Parse("--dotnet-isolated-debug");

        StartHostConfiguration configuration = contributor.Configure(parseResult);

        configuration.EnvironmentVariables.Should().Contain(
            DotNetStartOptionContributor.DebuggerWaitEnvironmentVariable, bool.TrueString);
        configuration.EnvironmentVariables.Should().Contain(
            DotNetStartOptionContributor.DeferredStartupHooksEnvironmentVariable, DotNetStartOptionContributor.WorkerStartupHook);
        configuration.EnvironmentVariables.Should().NotContainKey(
            DotNetStartOptionContributor.JsonOutputEnvironmentVariable);
        configuration.EnvironmentVariables.Should().NotContainKey(
            DotNetStartOptionContributor.StartupHooksEnvironmentVariable);
        configuration.StartupNotice.Should().Be(DotNetStartOptionContributor.DebuggerWaitNotice);
        configuration.OutputInterceptor.Should().NotBeNull();
    }

    [Fact]
    public void Configure_WithEnableJsonOutput_SetsJsonOutputAndStartupHook()
    {
        (DotNetStartOptionContributor contributor, RootCommand root) = CreateContributor();
        ParseResult parseResult = root.Parse("--enable-json-output");

        StartHostConfiguration configuration = contributor.Configure(parseResult);

        configuration.EnvironmentVariables.Should().Contain(
            DotNetStartOptionContributor.JsonOutputEnvironmentVariable, bool.TrueString);
        configuration.EnvironmentVariables.Should().Contain(
            DotNetStartOptionContributor.DeferredStartupHooksEnvironmentVariable, DotNetStartOptionContributor.WorkerStartupHook);
        configuration.EnvironmentVariables.Should().NotContainKey(
            DotNetStartOptionContributor.DebuggerWaitEnvironmentVariable);
        configuration.StartupNotice.Should().BeNull();
        configuration.OutputInterceptor.Should().NotBeNull();
    }

    [Fact]
    public void Configure_WithJsonOutputFile_ImpliesJsonOutputAndCreatesInterceptor()
    {
        (DotNetStartOptionContributor contributor, RootCommand root) = CreateContributor();
        ParseResult parseResult = root.Parse(["--json-output-file", "out.json"]);

        StartHostConfiguration configuration = contributor.Configure(parseResult);

        configuration.EnvironmentVariables.Should().Contain(
            DotNetStartOptionContributor.JsonOutputEnvironmentVariable, bool.TrueString);
        configuration.EnvironmentVariables.Should().ContainKey(
            DotNetStartOptionContributor.DeferredStartupHooksEnvironmentVariable);
        configuration.OutputInterceptor.Should().NotBeNull()
            .And.BeOfType<DotNetHostOutputInterceptor>();
    }
}
