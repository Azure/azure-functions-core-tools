// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class WorkloadManagementHostTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-management-graph-").FullName;
    private readonly NativeTemplateWorkflowHostFactory _factory = new();
    private readonly TestInteractionService _interaction = new();

    public static TheoryData<string[], bool> CommandCases =>
        new()
        {
            { ["workload"], true },
            { ["workload", "uninstall", "Example", "-e", "-v", "1.0.0"], true },
            { ["workload", "prune", "Example", "-e"], true },
            { ["--verbose=false", "workload", "list", "--json"], true },
            { ["workload", "install", "missing.nupkg"], true },
            { ["workload", "--help"], true },
            { ["help", "workload"], true },
            { ["help"], false },
            { ["version"], false },
            { ["new", ".", "--name", "workload"], false },
            { ["init", "workload"], false },
            { ["plugin", "workload", "uninstall", "Example"], false },
            { ["start", "--language", "workload"], false },
        };

    [Theory]
    [MemberData(nameof(CommandCases))]
    public void Classifier_ActualSharedCommandGraph_SelectsOnlyReservedWorkloadManagement(string[] arguments, bool expected)
    {
        var builder = CliHostFactory.CreateBuilder(_interaction);
        var files = Substitute.For<IFileSystem>();
        var native = Substitute.For<ITemplateStore>();
        builder.Services.AddSingleton(files);
        builder.Services.AddSingleton(native);

        bool result = _factory.IsWorkloadManagementCommand(builder, arguments);

        result.Should().Be(expected);
        files.ReceivedCalls().Should().BeEmpty();
        native.ReceivedCalls().Should().BeEmpty();
        _interaction.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Classifier_ActualBuiltInAliasAndResponseFile_UseTheSameGraphRatherThanTokenPresence()
    {
        var builder = CliHostFactory.CreateBuilder(_interaction);
        var descriptor = builder.Services.Single(service => service.ImplementationType == typeof(WorkloadCommand));
        builder.Services.Remove(descriptor);
        builder.Services.AddSingleton<FuncCliCommand>(provider =>
        {
            var command = ActivatorUtilities.CreateInstance<WorkloadCommand>(provider);
            command.Aliases.Add("wl");
            return command;
        });
        string response = Path.Combine(_root, "management.rsp");
        File.WriteAllText(response, "--verbose=false wl prune Example --exact");
        string nonManagement = Path.Combine(_root, "runtime.rsp");
        File.WriteAllText(nonManagement, "new . --name workload");

        _factory.IsWorkloadManagementCommand(builder, ["wl", "prune", "Example"]).Should().BeTrue();
        _factory.IsWorkloadManagementCommand(builder, ["@" + response]).Should().BeTrue();
        _factory.IsWorkloadManagementCommand(builder, ["@" + nonManagement]).Should().BeFalse();
    }

    [Fact]
    public void Classifier_ProviderLifetime_DisposesOnlyItsOwnedInstancesAndDoesNotMutateOriginalDescriptors()
    {
        var builder = CliHostFactory.CreateBuilder(_interaction);
        var shared = new LifetimeSentinel();
        List<LifetimeSentinel> owned = [];
        builder.Services.AddSingleton(shared);
        builder.Services.AddKeyedSingleton<LifetimeSentinel>("owned", (_, _) =>
        {
            var sentinel = new LifetimeSentinel();
            owned.Add(sentinel);
            return sentinel;
        });
        builder.Services.AddSingleton<FuncCliCommand>(provider =>
        {
            _ = provider.GetRequiredService<LifetimeSentinel>();
            _ = provider.GetRequiredKeyedService<LifetimeSentinel>("owned");
            return new LifetimeProbeCommand();
        });
        var descriptors = builder.Services.ToArray();

        _factory.IsWorkloadManagementCommand(builder, ["workload", "list"]).Should().BeTrue();

        shared.Disposed.Should().BeFalse();
        owned.Should().ContainSingle().Which.Disposed.Should().BeTrue();
        builder.Services.Should().Equal(descriptors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classifier_HostFactoryBackedConfiguration_RemainsOwnedByOriginalBuilder(bool management)
    {
        var builder = CliHostFactory.CreateBuilder(_interaction);
        using var owner = (IDisposable)builder.Configuration;
        var configuration = new DisposalSensitiveConfigurationProvider();
        ((IConfigurationBuilder)builder.Configuration).Add(new ConfigurationProbeSource(configuration));
        builder.Services.AddSingleton<FuncCliCommand>(provider =>
        {
            provider.GetServices<IConfiguration>().Should().ContainSingle().Which.Should().BeSameAs(builder.Configuration);
            return new LifetimeProbeCommand();
        });
        string[] arguments = management ? ["workload", "list"] : ["version"];

        _factory.IsWorkloadManagementCommand(builder, arguments).Should().Be(management);

        configuration.Disposed.Should().BeFalse("the copied host configuration factory returns the original shared configuration");
        builder.Configuration["probe:alive"].Should().Be("ready");
        owner.Dispose();
        configuration.Disposed.Should().BeTrue("the actual builder still owns its configuration");
    }

    [Fact]
    public void Classifier_NullContracts_AreProgrammerErrors()
    {
        var builder = CliHostFactory.CreateBuilder(_interaction);
        Action missingServices = () => _factory.IsWorkloadManagementCommand(null!, []);
        Action missingArguments = () => _factory.IsWorkloadManagementCommand(builder, null!);
        missingServices.Should().Throw<ArgumentNullException>();
        missingArguments.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parser_ExternalAliasCannotClaimReservedBuiltInIdentity()
    {
        var owner = TestWorkloads.CreateInfo("External.Owner");
        var external = new ExternalCommand(owner, new TestWorkloads.StubFuncCommand("external"));
        external.Aliases.Add("workload");
        using var services = (ServiceProvider)TestParser.BuildServiceProviderWith(_interaction, collection =>
            collection.AddSingleton<FuncCliCommand>(external));

        var root = Parser.CreateCommand(services);

        root.Subcommands.Should().NotContain(external);
        root.Parse(["workload", "list"]).CommandResult.Command.Should().BeOfType<WorkloadListCommand>();
        _interaction.Lines.Should().Contain(line => line.Contains("External.Owner", StringComparison.Ordinal)
            && line.Contains("'workload'", StringComparison.Ordinal) && line.Contains("reserved", StringComparison.Ordinal));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class LifetimeSentinel : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class DisposalSensitiveConfigurationProvider : ConfigurationProvider, IDisposable
    {
        public bool Disposed { get; private set; }
        public override void Load() => Data["probe:alive"] = "ready";
        public override bool TryGet(string key, out string? value)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return base.TryGet(key, out value);
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class ConfigurationProbeSource(DisposalSensitiveConfigurationProvider provider) : IConfigurationSource
    {
        private readonly DisposalSensitiveConfigurationProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;
    }

    private sealed class LifetimeProbeCommand() : FuncCliCommand("lifetime-probe", "Resolves classification lifetime controls."), IBuiltInCommand;
}
