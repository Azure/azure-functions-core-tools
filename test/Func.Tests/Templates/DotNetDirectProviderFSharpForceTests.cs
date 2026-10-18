// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class DotNetDirectProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FSharpForce_CancellationBeforePreparedInvocation_PreservesSelectedTargetAndProject(bool preview)
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        string target = Path.Combine(fixture.Directory.Info.FullName, "Replace.fs");
        File.WriteAllText(target, "selected user bytes");
        var context = ItemContext(fixture, selected, Parse(selected, []), "Replace");
        context = context with { Command = context.Command with { Force = true }, Invocation = context.Invocation with { Force = true } };
        var prepared = await fixture.Provider.PrepareAsync(context, _deadline.Token);
        prepared.Request.Should().NotBeNull();
        byte[] project = File.ReadAllBytes(fixture.Project!);

        Func<Task> invoke = () => selected.InvokeAsync(prepared.Request! with { DryRun = preview }, new CancellationToken(canceled: true));

        await invoke.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(target).Should().Be("selected user bytes");
        File.ReadAllBytes(fixture.Project!).Should().Equal(project);
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("target")]
    [InlineData("project")]
    [InlineData("source")]
    [InlineData("renderedHash")]
    [InlineData("removedForce")]
    [InlineData("removedSourceProof")]
    public async Task FSharpForce_PreparedContractDrift_RefusesBeforeReplacingTargetOrRunningDependencies(string changed)
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        string target = Path.Combine(fixture.Directory.Info.FullName, "Replace.fs");
        File.WriteAllText(target, "original selected source");
        var context = ItemContext(fixture, selected, Parse(selected, []), "Replace");
        context = context with { Command = context.Command with { Force = true }, Invocation = context.Invocation with { Force = true } };
        var prepared = await fixture.Provider.PrepareAsync(context, _deadline.Token);
        prepared.Request.Should().NotBeNull(string.Join(" ", prepared.Diagnostics));
        var request = prepared.Request!;
        if (changed == "target") File.AppendAllText(target, " user changed target");
        if (changed == "project") File.AppendAllText(fixture.Project!, "\n<!-- user changed project -->");
        if (changed == "source")
        {
            var originalRead = selected.ReadPackageFileAsync;
            ResolvedTemplate drift = new(selected.Entry, selected.InvokeAsync,
                (path, token) => path == "content/HttpTrigger-FSharp/HttpTriggerFSharp.fs"
                    ? Task.FromResult<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes("changed"))
                    : originalRead(path, token),
                selected.ConfigurationSha256, selected.ValidateConfigurationAsync, selected.ConfigurationSourceIdentity);
            var sourcePrepared = await fixture.Provider.PrepareAsync(context with { SelectedTemplate = drift }, _deadline.Token);
            sourcePrepared.Request.Should().BeNull();
            sourcePrepared.Diagnostics.Should().ContainSingle().Which.Should().Contain("source");
            File.ReadAllText(target).Should().Be("original selected source");
            fixture.Runner.Commands.Should().BeEmpty();
            return;
        }
        if (changed == "renderedHash")
        {
            TemplateFSharpCompileEffect previous = request.EffectSupport!.FSharpRepair!;
            TemplateFSharpCompileEffect tampered = new(previous.Source, previous.ProjectPath, previous.ExpectedProjectSha256,
                previous.EvaluatedCompileOrder, [new(target, new string('0', 64), previous.GeneratedFiles[0].ExpectedTargetSha256)]);
            request = request with { EffectSupport = request.EffectSupport with { FSharpRepair = tampered } };
        }
        if (changed == "removedForce") request = request with { Force = false };
        if (changed == "removedSourceProof") request = request with { SourceSnapshot = null };
        var before = Directory.GetFiles(fixture.Directory.Info.FullName, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        TemplateInvocationResult result = await selected.InvokeAsync(request, _deadline.Token);

        result.Status.Should().NotBe(TemplateInvocationStatus.Success);
        foreach (var file in before)
        {
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        }
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FSharpForce_ExistingUncompiledRegularFile_InsertsOnlyItsQualifiedCompileEntry(bool preview)
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        string target = Path.Combine(fixture.Directory.Info.FullName, "Replace.fs");
        File.WriteAllText(target, "original uncompiled selected source");
        var context = ItemContext(fixture, selected, Parse(selected, []), "Replace");
        context = context with { Command = context.Command with { Force = true }, Invocation = context.Invocation with { Force = true } };
        var prepared = await fixture.Provider.PrepareAsync(context, _deadline.Token);
        prepared.Request.Should().NotBeNull(string.Join(" ", prepared.Diagnostics));
        TemplateFSharpCompileEffect repair = prepared.Request!.EffectSupport!.FSharpRepair!;
        repair.GeneratedFiles.Should().ContainSingle().Which.ExpectedTargetSha256.Should().BeEquivalentTo(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("original uncompiled selected source"))));
        byte[] project = File.ReadAllBytes(fixture.Project!);

        var result = await selected.InvokeAsync(prepared.Request with { DryRun = preview }, _deadline.Token);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        if (preview)
        {
            File.ReadAllText(target).Should().Be("original uncompiled selected source");
            File.ReadAllBytes(fixture.Project!).Should().Equal(project);
            fixture.Runner.Commands.Should().BeEmpty();
        }
        else
        {
            File.ReadAllText(target).Should().Contain("module Replace");
            File.ReadAllText(fixture.Project!).Should().Contain("<Compile Include=\"Replace.fs\" />");
        }
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("ancestor")]
    public async Task FSharpForce_UnsupportedTargetKinds_RefuseBeforeReplacement(string kind)
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        string target = Path.Combine(fixture.Directory.Info.FullName, "Replace.fs");
        if (kind == "directory") Directory.CreateDirectory(target);
        else File.WriteAllText(Path.Combine(fixture.Directory.Info.FullName, "blocked"), "regular-file ancestor");
        var context = ItemContext(fixture, selected, Parse(selected, []), kind == "ancestor" ? "blocked/Replace" : "Replace");
        context = context with { Command = context.Command with { Force = true }, Invocation = context.Invocation with { Force = true } };
        byte[] project = File.ReadAllBytes(fixture.Project!);

        var prepared = await fixture.Provider.PrepareAsync(context, _deadline.Token);

        prepared.Request.Should().BeNull();
        File.ReadAllBytes(fixture.Project!).Should().Equal(project);
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [SymbolicLinkFact]
    public async Task FSharpForce_ReplacementBecomesLink_RefusesAndPreservesExternalContent()
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        string target = Path.Combine(fixture.Directory.Info.FullName, "Replace.fs");
        File.WriteAllText(target, "selected");
        var context = ItemContext(fixture, selected, Parse(selected, []), "Replace");
        context = context with { Command = context.Command with { Force = true }, Invocation = context.Invocation with { Force = true } };
        var prepared = await fixture.Provider.PrepareAsync(context, _deadline.Token);
        prepared.Request.Should().NotBeNull();
        string outside = Path.Combine(_root, "external.fs");
        File.WriteAllText(outside, "external sentinel");
        File.Delete(target);
        File.CreateSymbolicLink(target, outside);

        var result = await selected.InvokeAsync(prepared.Request!, _deadline.Token);

        result.Status.Should().NotBe(TemplateInvocationStatus.Success);
        File.ReadAllText(outside).Should().Be("external sentinel");
        fixture.Runner.Commands.Should().BeEmpty();
    }
}
