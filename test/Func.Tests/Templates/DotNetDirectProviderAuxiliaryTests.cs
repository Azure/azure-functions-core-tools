// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class DotNetDirectProviderTests
{
    [Theory]
    [InlineData("DurableFunctionsEntityClass.1.x", 128)]
    [InlineData("DurableFunctionsEntityFunction.1.x", 128)]
    [InlineData("DurableFunctionsOrchestration.1.x", 118)]
    [InlineData("SignalR.3.x", 128)]
    [InlineData("DaprPublishOutputBinding", 115)]
    [InlineData("DaprServiceInvocationTrigger", 121)]
    public async Task AuxiliaryProvider_ActualNativeNamesAndHostBounds_RefusesBeforeProjectWrites(string variant, int maximum)
    {
        using var fixture = await OpenAsync("C#");
        var selected = (await fixture.Templater.ResolveGroupAsync("Azure.Function.CSharp.Isolated." + variant,
            TemplateType.Item, _deadline.Token)).Group!.Single();
        var parsed = Parse(selected, ["--namespace", "Explicit.Functions"]);
        byte[] project = File.ReadAllBytes(fixture.Project!);
        (string? Name, bool Allowed, string? Actual)[] cases =
        [
            (null, true, selected.Entry.DefaultName),
            ("PlainName", true, "PlainName"),
            ("Space Name", true, "Space_Name"),
            ("Hyphen-Name", true, "Hyphen_Name"),
            ("\u96EA", false, null),
            ("1Start", false, null),
            ("_Start", false, null),
            ("host", false, null),
            ("HOST", false, null),
            (new string('A', maximum), true, new string('A', maximum)),
            (new string('B', maximum + 1), false, null),
        ];
        foreach ((string? name, bool allowed, string? actual) in cases)
        {
            var before = Directory.GetFiles(fixture.Directory.Info.FullName).Order(StringComparer.Ordinal).ToArray();

            var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, parsed, name), _deadline.Token);

            if (allowed)
            {
                prepared.Diagnostics.Should().BeEmpty($"{variant}: {name}");
                var request = prepared.Request!;
                request.SourceSnapshot.Should().NotBeNull();
                var capture = await selected.CaptureNativePreparationAsync(request, new PhysicalFileSystem(), true, _deadline.Token);
                capture.Failure.Should().BeNull();
                string source = Encoding.UTF8.GetString(capture.Snapshot!.ExpandedFiles.Single().Value.Span);
                source.Should().Contain("class " + actual);
                source.Should().Contain("namespace Explicit.Functions");
                DotNetAuxiliaryNameValidation.Validate(selected.Entry.Identity, capture.Snapshot).Should().BeNull();
                var preview = await selected.InvokeAsync(request with { DryRun = true }, _deadline.Token);
                preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
            }
            else
            {
                prepared.Request.Should().BeNull($"{variant}: {name}");
                string.Join("; ", prepared.Diagnostics).Should().Contain("valid Azure Functions name");
            }
            Directory.GetFiles(fixture.Directory.Info.FullName).Order(StringComparer.Ordinal).Should().Equal(before);
            File.ReadAllBytes(fixture.Project!).Should().Equal(project);
            fixture.Runner.Commands.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AuxiliarySources_RawOlderSource_RemainsCompatibleButUnknownSourceRefuses()
    {
        using var fixture = await OpenAsync("C#");
        var selected = (await fixture.Templater.ResolveGroupAsync("Azure.Function.CSharp.Isolated.DaprServiceInvocationTrigger",
            TemplateType.Item, _deadline.Token)).Group!.Single();
        const string path = "content/DaprServiceInvocationTrigger-CSharp-Isolated/DaprServiceInvocationTriggerCSharp.cs";
        var original = await selected.ReadPackageFileAsync("provenance/upstream-content/" + path, _deadline.Token);
        ResolvedTemplate older = new(selected.Entry, selected.InvokeAsync,
            (requested, token) => requested == path ? Task.FromResult(original) : selected.ReadPackageFileAsync(requested, token),
            selected.ConfigurationSha256, selected.ValidateConfigurationAsync, selected.ConfigurationSourceIdentity);
        DotNetTemplateSources sources = new();

        var raw = await sources.QualifyAuxiliaryAsync(older, _deadline.Token);

        raw.Should().BeNull("older source-qualified packages keep their original operation limits, without latest enforcement");
        ResolvedTemplate changed = new(selected.Entry, selected.InvokeAsync,
            (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes("changed")),
            selected.ConfigurationSha256, selected.ValidateConfigurationAsync, selected.ConfigurationSourceIdentity);
        var action = () => sources.QualifyAuxiliaryAsync(changed, _deadline.Token);
        await action.Should().ThrowAsync<InvalidDataException>().WithMessage("*source changed or is unsupported*");
        ResolvedTemplate decoy = new(selected.Entry, selected.InvokeAsync, selected.ReadPackageFileAsync,
            selected.ConfigurationSha256, selected.ValidateConfigurationAsync, "archive::/decoy/.template.config/template.json");
        var decoyAction = () => sources.QualifyAuxiliaryAsync(decoy, _deadline.Token);
        await decoyAction.Should().ThrowAsync<InvalidDataException>().WithMessage("*qualified source path*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuxiliarySources_Composition_PreservesExistingPinsAndRefusesConflicts(bool conflicting)
    {
        const string identity = "Azure.Function.CSharp.Isolated.SignalR.3.x";
        string hash = new('A', 64);
        TemplateEffectSource source = new(identity, "content/Item.cs", hash);
        TemplateEffectSource configuration = new(identity, "content/.template.config/template.json", hash);
        NativeTemplateSourceSnapshot existing = new(identity, hash, [configuration, source], [new("Item.cs", source)]);
        TemplateEffectSource auxiliary = source with { Sha256 = conflicting ? new('B', 64) : hash };
        NativeTemplateSourceSnapshot additional = new(identity, hash, [auxiliary], [new("Item.cs", auxiliary)]);

        var action = () => DotNetTemplateSources.CombineAuxiliarySources(existing, additional);

        if (conflicting)
        {
            action.Should().Throw<InvalidDataException>().WithMessage("*hashes disagree*");
        }
        else
        {
            var combined = action();
            combined.Should().NotBeNull();
            combined!.Sources.Should().Equal(configuration, source);
        }
        DotNetTemplateSources.CombineAuxiliarySources(existing, null).Should().BeSameAs(existing);
        DotNetTemplateSources.CombineAuxiliarySources(null, additional).Should().BeSameAs(additional);
    }
}
