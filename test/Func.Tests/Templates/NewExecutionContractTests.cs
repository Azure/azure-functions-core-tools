// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NewExecutionContractTests
{
    private readonly INewExecutionContextResolver _contexts = Substitute.For<INewExecutionContextResolver>();
    private readonly ITemplaterFactory _factory = Substitute.For<ITemplaterFactory>();
    private readonly ITemplateArgumentParser _arguments = new TemplateArgumentParser();
    private readonly IInteractionService _interaction = Substitute.For<IInteractionService>();

    [Fact]
    public void Constructors_NullDependencies_RejectProgrammerErrors()
    {
        TemplateExecutionInteraction prompts = new(_arguments, _interaction);
        Action context = () => _ = new NewExecutionRunner(null!, _factory, _arguments, _interaction, prompts);
        Action factory = () => _ = new NewExecutionRunner(_contexts, null!, _arguments, _interaction, prompts);
        Action arguments = () => _ = new NewExecutionRunner(_contexts, _factory, null!, _interaction, prompts);
        Action interaction = () => _ = new NewExecutionRunner(_contexts, _factory, _arguments, null!, prompts);
        Action nullPrompts = () => _ = new NewExecutionRunner(_contexts, _factory, _arguments, _interaction, null!);
        Action renderer = () => _ = new NewExecutionRenderer(null!);
        Action command = () => _ = new NewExecutionCommand(null!, new(_interaction), new([]));

        context.Should().Throw<ArgumentNullException>().WithParameterName("contexts");
        factory.Should().Throw<ArgumentNullException>().WithParameterName("factory");
        arguments.Should().Throw<ArgumentNullException>().WithParameterName("arguments");
        interaction.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
        nullPrompts.Should().Throw<ArgumentNullException>().WithParameterName("prompts");
        renderer.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
        command.Should().Throw<ArgumentNullException>().WithParameterName("runner");
    }

    [Fact]
    public void ContextResolver_NullDependencies_RejectsEachConstructorArgument()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var projects = Substitute.For<IFunctionsProjectResolver>();
        var profiles = Substitute.For<IProfileResolver>();
        var stackOptions = Substitute.For<IOptionsMonitor<StackOptions>>();
        var hostReader = Substitute.For<IHostJsonBundleSectionReader>();
        var bundles = Substitute.For<IExtensionBundleResolver>();
        Action nullFileSystem = () => _ = new NewExecutionContextResolver(null!, projects, profiles, stackOptions, [], hostReader, bundles);
        Action nullProjects = () => _ = new NewExecutionContextResolver(fileSystem, null!, profiles, stackOptions, [], hostReader, bundles);
        Action nullProfiles = () => _ = new NewExecutionContextResolver(fileSystem, projects, null!, stackOptions, [], hostReader, bundles);
        Action nullStackOptions = () => _ = new NewExecutionContextResolver(fileSystem, projects, profiles, null!, [], hostReader, bundles);
        Action nullInitializers = () => _ = new NewExecutionContextResolver(fileSystem, projects, profiles, stackOptions, null!, hostReader, bundles);
        Action nullHostReader = () => _ = new NewExecutionContextResolver(fileSystem, projects, profiles, stackOptions, [], null!, bundles);
        Action nullBundles = () => _ = new NewExecutionContextResolver(fileSystem, projects, profiles, stackOptions, [], hostReader, null!);

        nullFileSystem.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
        nullProjects.Should().Throw<ArgumentNullException>().WithParameterName("projects");
        nullProfiles.Should().Throw<ArgumentNullException>().WithParameterName("profiles");
        nullStackOptions.Should().Throw<ArgumentNullException>().WithParameterName("stackOptions");
        nullInitializers.Should().Throw<ArgumentNullException>().WithParameterName("initializers");
        nullHostReader.Should().Throw<ArgumentNullException>().WithParameterName("hostReader");
        nullBundles.Should().Throw<ArgumentNullException>().WithParameterName("bundles");
    }

    [Fact]
    public async Task ExecuteAsync_NullRequestOrAvailability_StopsBeforeContextAndFactory()
    {
        NewExecutionRunner runner = new(_contexts, _factory, _arguments, _interaction, new(_arguments, _interaction));
        NewExecutionRequest request = new(WorkingDirectory.FromCwd(), null, null, null, false, true, false, false, false, [], new HashSet<string>());

        Func<Task> nullRequest = () => runner.ExecuteAsync(null!, new([]), CancellationToken.None);
        Func<Task> nullAvailability = () => runner.ExecuteAsync(request, null!, CancellationToken.None);

        await nullRequest.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
        await nullAvailability.Should().ThrowAsync<ArgumentNullException>().WithParameterName("availability");
        _contexts.ReceivedCalls().Should().BeEmpty();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PreCanceled_StopsBeforeContextOrFactory()
    {
        NewExecutionRunner runner = new(_contexts, _factory, _arguments, _interaction, new(_arguments, _interaction));
        NewExecutionRequest request = new(WorkingDirectory.FromCwd(), null, null, null, false, true, false, false, false, [], new HashSet<string>());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> act = () => runner.ExecuteAsync(request, new([]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _contexts.ReceivedCalls().Should().BeEmpty();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Render_NullResult_RejectsBeforeOutput()
    {
        NewExecutionRenderer renderer = new(_interaction);

        Action act = () => renderer.Render(null!, jsonOutput: true);

        act.Should().Throw<ArgumentNullException>().WithParameterName("result");
        _interaction.ReceivedCalls().Should().BeEmpty();
    }
}
