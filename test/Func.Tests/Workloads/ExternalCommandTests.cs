// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests.Workloads;

public class ExternalCommandTests
{
    [Fact]
    public void Ctor_CopiesNameAndDescription()
    {
        var source = new TestWorkloads.StubFuncCommand("deploy", "Deploy the app.");
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        external.Name.Should().Be("deploy");
        external.Description.Should().Be("Deploy the app.");
    }

    [Fact]
    public void Ctor_TranslatesOptionsToParser()
    {
        var stack = new FuncCommandOption<string>("--stack", "-s", "Project stack");
        var verbose = new FuncCommandOption<bool>("--verbose", null, "Verbose output");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [stack, verbose]);

        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        var parserOptions = external.Options.Select(o => o.Name).ToList();
        parserOptions.Should().Contain("--stack");
        parserOptions.Should().Contain("--verbose");

        var stackOption = external.Options.Single(o => o.Name == "--stack");
        stackOption.Aliases.Should().Contain("-s");
    }

    [Fact]
    public void Ctor_DefaultValue_FlowsThroughToParser()
    {
        var port = new FuncCommandOption<int>("--port", "-p", "Port", defaultValue: 7071);
        var source = new TestWorkloads.StubFuncCommand("start", options: [port]);
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        var typed = (Option<int>)external.Options.Single(o => o.Name == "--port");
        var parseResult = external.Parse(string.Empty);
        parseResult.GetValue(typed).Should().Be(7071);
    }

    [Fact]
    public void Ctor_TranslatesArgumentsAndArity()
    {
        var optional = new FuncCommandArgument<string>("path", "Path");
        var required = new FuncCommandArgument<string>("name", "Name", isRequired: true);
        var source = new TestWorkloads.StubFuncCommand("deploy", arguments: [optional, required]);

        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        var pathArg = external.Arguments.Single(a => a.Name == "path");
        var nameArg = external.Arguments.Single(a => a.Name == "name");
        pathArg.Arity.Should().Be(ArgumentArity.ZeroOrOne);
        nameArg.Arity.Should().Be(ArgumentArity.ExactlyOne);
    }

    [Fact]
    public void Ctor_TranslatesSubcommandsRecursively()
    {
        var leaf = new TestWorkloads.StubFuncCommand("leaf");
        var middle = new TestWorkloads.StubFuncCommand("middle", subcommands: [leaf]);
        var top = new TestWorkloads.StubFuncCommand("top", subcommands: [middle]);

        var external = new ExternalCommand(TestWorkloads.CreateInfo(), top);

        var middleSub = external.Subcommands.Should().ContainSingle().Subject;
        middleSub.Name.Should().Be("middle");
        middleSub.Subcommands.Should().ContainSingle().Which.Name.Should().Be("leaf");
    }

    [Fact]
    public void Ctor_DuplicateOptionName_Throws()
    {
        var a = new FuncCommandOption<string>("--name", null, "first");
        var b = new FuncCommandOption<string>("--name", null, "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [a, b]);

        var ex = FluentActions.Invoking(() => new ExternalCommand(TestWorkloads.CreateInfo("Workload.A"), source)).Should().ThrowExactly<WorkloadOperationException>().Which;
        ex.Message.Should().Contain("Workload.A");
        ex.Message.Should().Contain("--name");
    }

    [Fact]
    public void Ctor_DuplicateOptionShortName_Throws()
    {
        var a = new FuncCommandOption<string>("--first", "-x", "first");
        var b = new FuncCommandOption<string>("--second", "-x", "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [a, b]);

        var ex = FluentActions.Invoking(() => new ExternalCommand(TestWorkloads.CreateInfo(), source)).Should().ThrowExactly<WorkloadOperationException>().Which;
        ex.Message.Should().Contain("-x");
    }

    [Fact]
    public void Ctor_DuplicateArgumentName_Throws()
    {
        var a = new FuncCommandArgument<string>("path", "first");
        var b = new FuncCommandArgument<string>("path", "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", arguments: [a, b]);

        var ex = FluentActions.Invoking(() => new ExternalCommand(TestWorkloads.CreateInfo(), source)).Should().ThrowExactly<WorkloadOperationException>().Which;
        ex.Message.Should().Contain("path");
    }

    [Fact]
    public void Ctor_DuplicateSubcommandName_Throws()
    {
        var a = new TestWorkloads.StubFuncCommand("dup");
        var b = new TestWorkloads.StubFuncCommand("dup");
        var source = new TestWorkloads.StubFuncCommand("parent", subcommands: [a, b]);

        var ex = FluentActions.Invoking(() => new ExternalCommand(TestWorkloads.CreateInfo(), source)).Should().ThrowExactly<WorkloadOperationException>().Which;
        ex.Message.Should().Contain("dup");
    }

    [Fact]
    public void Ctor_WorkloadOperationException_CarriesWorkloadInfo()
    {
        var a = new FuncCommandOption<string>("--name", null, "first");
        var b = new FuncCommandOption<string>("--name", null, "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [a, b]);
        var workload = TestWorkloads.CreateInfo("Workload.B");

        var ex = FluentActions.Invoking(() => new ExternalCommand(workload, source)).Should().ThrowExactly<WorkloadOperationException>().Which;
        ex.Workload.Should().BeSameAs(workload);
    }

    [Fact]
    public void Ctor_NullSourceCommand_Throws()
    {
        FluentActions.Invoking(() => new ExternalCommand(TestWorkloads.CreateInfo(), null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullWorkload_Throws()
    {
        var source = new TestWorkloads.StubFuncCommand("ok");
        FluentActions.Invoking(() => new ExternalCommand(null!, source)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_PassesParsedOptionValues()
    {
        var stack = new FuncCommandOption<string>("--stack", "-s", "Stack");
        string? captured = null;
        var source = new TestWorkloads.StubFuncCommand(
            "deploy",
            options: [stack],
            execute: (ctx, _) =>
            {
                captured = ctx.GetValue(stack);
                return Task.FromResult(0);
            });
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        var parseResult = external.Parse("--stack dotnet");
        var exit = await parseResult.InvokeAsync();

        exit.Should().Be(0);
        captured.Should().Be("dotnet");
    }

    [Fact]
    public async Task ExecuteAsync_PassesParsedArgumentValues()
    {
        var name = new FuncCommandArgument<string>("name", "Name", isRequired: true);
        string? captured = null;
        var source = new TestWorkloads.StubFuncCommand(
            "deploy",
            arguments: [name],
            execute: (ctx, _) =>
            {
                captured = ctx.GetValue(name);
                return Task.FromResult(0);
            });
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        await external.Parse("my-app").InvokeAsync();

        captured.Should().Be("my-app");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultValue_AppliedWhenOptionOmitted()
    {
        var port = new FuncCommandOption<int>("--port", null, "Port", defaultValue: 7071);
        int captured = -1;
        var source = new TestWorkloads.StubFuncCommand(
            "start",
            options: [port],
            execute: (ctx, _) =>
            {
                captured = ctx.GetValue(port);
                return Task.FromResult(0);
            });
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        await external.Parse(string.Empty).InvokeAsync();

        captured.Should().Be(7071);
    }

    [Fact]
    public async Task ExecuteAsync_GetValue_UsesReferenceIdentityOnDescriptor()
    {
        var declared = new FuncCommandOption<string>("--name", null, "Name");
        var lookalike = new FuncCommandOption<string>("--name", null, "Name");
        Exception? captured = null;

        var source = new TestWorkloads.StubFuncCommand(
            "deploy",
            options: [declared],
            execute: (ctx, _) =>
            {
                try
                {
                    ctx.GetValue(lookalike);
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                return Task.FromResult(0);
            });
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        await external.Parse("--name foo").InvokeAsync();

        captured.Should().BeOfType<ArgumentException>();
        captured!.Message.Should().Contain("--name");
    }

    [Fact]
    public async Task ExecuteAsync_GetValue_NullDescriptor_Throws()
    {
        Exception? captured = null;
        var source = new TestWorkloads.StubFuncCommand(
            "deploy",
            execute: (ctx, _) =>
            {
                try
                {
                    ctx.GetValue<string>((FuncCommandOption<string>)null!);
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                return Task.FromResult(0);
            });
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        await external.Parse(string.Empty).InvokeAsync();

        captured.Should().BeOfType<ArgumentNullException>();
    }
}
