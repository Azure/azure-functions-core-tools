// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class ExternalCommandTests
{
    [Fact]
    public void Ctor_CopiesNameAndDescription()
    {
        var source = new TestWorkloads.StubFuncCommand("deploy", "Deploy the app.");
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        Assert.Equal("deploy", external.Name);
        Assert.Equal("Deploy the app.", external.Description);
    }

    [Fact]
    public void Ctor_TranslatesOptionsToParser()
    {
        var stack = new FuncCommandOption<string>("--stack", "-s", "Project stack");
        var verbose = new FuncCommandOption<bool>("--verbose", null, "Verbose output");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [stack, verbose]);

        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        var parserOptions = external.Options.Select(o => o.Name).ToList();
        Assert.Contains("--stack", parserOptions);
        Assert.Contains("--verbose", parserOptions);

        var stackOption = external.Options.Single(o => o.Name == "--stack");
        Assert.Contains("-s", stackOption.Aliases);
    }

    [Fact]
    public void Ctor_DefaultValue_FlowsThroughToParser()
    {
        var port = new FuncCommandOption<int>("--port", "-p", "Port", defaultValue: 7071);
        var source = new TestWorkloads.StubFuncCommand("start", options: [port]);
        var external = new ExternalCommand(TestWorkloads.CreateInfo(), source);

        var typed = (Option<int>)external.Options.Single(o => o.Name == "--port");
        var parseResult = external.Parse(string.Empty);
        Assert.Equal(7071, parseResult.GetValue(typed));
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
        Assert.Equal(ArgumentArity.ZeroOrOne, pathArg.Arity);
        Assert.Equal(ArgumentArity.ExactlyOne, nameArg.Arity);
    }

    [Fact]
    public void Ctor_TranslatesSubcommandsRecursively()
    {
        var leaf = new TestWorkloads.StubFuncCommand("leaf");
        var middle = new TestWorkloads.StubFuncCommand("middle", subcommands: [leaf]);
        var top = new TestWorkloads.StubFuncCommand("top", subcommands: [middle]);

        var external = new ExternalCommand(TestWorkloads.CreateInfo(), top);

        var middleSub = Assert.Single(external.Subcommands);
        Assert.Equal("middle", middleSub.Name);
        var leafSub = Assert.Single(middleSub.Subcommands);
        Assert.Equal("leaf", leafSub.Name);
    }

    [Fact]
    public void Ctor_DuplicateOptionName_Throws()
    {
        var a = new FuncCommandOption<string>("--name", null, "first");
        var b = new FuncCommandOption<string>("--name", null, "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [a, b]);

        var ex = Assert.Throws<WorkloadOperationException>(
            () => new ExternalCommand(TestWorkloads.CreateInfo("Workload.A"), source));
        Assert.Contains("Workload.A", ex.Message);
        Assert.Contains("--name", ex.Message);
    }

    [Fact]
    public void Ctor_DuplicateOptionShortName_Throws()
    {
        var a = new FuncCommandOption<string>("--first", "-x", "first");
        var b = new FuncCommandOption<string>("--second", "-x", "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [a, b]);

        var ex = Assert.Throws<WorkloadOperationException>(
            () => new ExternalCommand(TestWorkloads.CreateInfo(), source));
        Assert.Contains("-x", ex.Message);
    }

    [Fact]
    public void Ctor_DuplicateArgumentName_Throws()
    {
        var a = new FuncCommandArgument<string>("path", "first");
        var b = new FuncCommandArgument<string>("path", "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", arguments: [a, b]);

        var ex = Assert.Throws<WorkloadOperationException>(
            () => new ExternalCommand(TestWorkloads.CreateInfo(), source));
        Assert.Contains("path", ex.Message);
    }

    [Fact]
    public void Ctor_DuplicateSubcommandName_Throws()
    {
        var a = new TestWorkloads.StubFuncCommand("dup");
        var b = new TestWorkloads.StubFuncCommand("dup");
        var source = new TestWorkloads.StubFuncCommand("parent", subcommands: [a, b]);

        var ex = Assert.Throws<WorkloadOperationException>(
            () => new ExternalCommand(TestWorkloads.CreateInfo(), source));
        Assert.Contains("dup", ex.Message);
    }

    [Fact]
    public void Ctor_WorkloadOperationException_CarriesWorkloadInfo()
    {
        var a = new FuncCommandOption<string>("--name", null, "first");
        var b = new FuncCommandOption<string>("--name", null, "second");
        var source = new TestWorkloads.StubFuncCommand("deploy", options: [a, b]);
        var workload = TestWorkloads.CreateInfo("Workload.B");

        var ex = Assert.Throws<WorkloadOperationException>(
            () => new ExternalCommand(workload, source));
        Assert.Same(workload, ex.Workload);
    }

    [Fact]
    public void Ctor_NullSourceCommand_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ExternalCommand(TestWorkloads.CreateInfo(), null!));
    }

    [Fact]
    public void Ctor_NullWorkload_Throws()
    {
        var source = new TestWorkloads.StubFuncCommand("ok");
        Assert.Throws<ArgumentNullException>(() => new ExternalCommand(null!, source));
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

        Assert.Equal(0, exit);
        Assert.Equal("dotnet", captured);
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

        Assert.Equal("my-app", captured);
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

        Assert.Equal(7071, captured);
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

        Assert.IsType<ArgumentException>(captured);
        Assert.Contains("--name", captured!.Message);
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

        Assert.IsType<ArgumentNullException>(captured);
    }
}
