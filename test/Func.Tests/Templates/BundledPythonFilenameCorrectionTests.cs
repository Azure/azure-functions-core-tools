// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;
using Azure.Functions.Cli.Templates.V2;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    private static readonly string[] _authorizedFilenameCorrections =
    [
        "DurableFunctionsEntityTrigger-Python",
        "DurableFunctionsOrchestration-Python",
        "EventGridTrigger-Python",
        "ServiceBusTopicTrigger-Python",
    ];

    [Theory]
    [InlineData("stable", 18)]
    [InlineData("preview", 24)]
    [InlineData("experimental", 23)]
    public void FilenameBeforeEvidence_PinnedLegacyCreateStillCharacterizesExactlyFourDoubleSuffixCases(string channel, int count)
    {
        var ids = _channels.RootElement.EnumerateArray().Single(item => item.GetProperty("channel").GetString() == channel)
            .GetProperty("templateIds").EnumerateArray().Select(item => item.GetString()!).ToList();
        ids.Should().HaveCount(count);
        List<string> doubleSuffix = [];
        foreach (var id in ids)
        {
            var source = _sources.Single(item => item.Id == id);
            var legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy-before", id));
            var result = new V2TemplateEngine().Apply(source, "BeforeEvidence",
                new Dictionary<string, string?> { ["trigger-functionName"] = "BeforeEvidence" }, legacy, false);

            result.Should().BeOfType<TemplateApplicationResult.Created>();
            var file = Directory.GetFiles(legacy.FullName).Should().ContainSingle().Subject;
            if (_authorizedFilenameCorrections.Contains(id))
            {
                Path.GetFileName(file).Should().Be("function_app.py.py");
                var createJob = source.Jobs!.Single(job => job.Type == "CreateNewApp");
                createJob.Inputs!.Single(input => input.AssignTo == "$(APP_FILENAME)").DefaultValue.Should().Be("function_app.py");
                source.Actions!.Single(action => action.Type == "WriteToFile" && createJob.Actions!.Contains(action.Name!))
                    .FilePath.Should().Be("$(APP_FILENAME).py");
                doubleSuffix.Add(id);
            }
            else
            {
                Path.GetFileName(file).Should().Be("function_app.py");
            }
        }

        doubleSuffix.Should().BeEquivalentTo(_authorizedFilenameCorrections);
    }

    [Theory]
    [InlineData("stable", "DurableFunctionsEntityTrigger-Python")]
    [InlineData("stable", "DurableFunctionsOrchestration-Python")]
    [InlineData("stable", "EventGridTrigger-Python")]
    [InlineData("stable", "ServiceBusTopicTrigger-Python")]
    [InlineData("preview", "DurableFunctionsEntityTrigger-Python")]
    [InlineData("preview", "DurableFunctionsOrchestration-Python")]
    [InlineData("preview", "EventGridTrigger-Python")]
    [InlineData("preview", "ServiceBusTopicTrigger-Python")]
    [InlineData("experimental", "DurableFunctionsEntityTrigger-Python")]
    [InlineData("experimental", "DurableFunctionsOrchestration-Python")]
    [InlineData("experimental", "EventGridTrigger-Python")]
    [InlineData("experimental", "ServiceBusTopicTrigger-Python")]
    [InlineData("stable", "HttpTrigger-Python")]
    public async Task FilenameCorrection_AuthorizedNativeCreateThenAppend_UsesSingleTargetAndPreservesSourceContent(string channel, string id)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync(channel));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var mapping = await ReadAppendMappingAsync(selected);
        var parameters = selected.Parameters.ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue);
        foreach (var explicitName in new[] { false, true })
        {
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "corrected", explicitName ? "explicit" : "default"));
            var app = Path.Combine(output.Info.FullName, "function_app.py");
            var capability = new OwnedPythonAppendCapability(selected, mapping.HelperSource, app);
            var support = AppendSupport(mapping, capability);
            var binding = await support.ResolveAsync(selected, CancellationToken.None);
            var name = await ResolveFilenameNameAsync(selected, explicitName ? "ChosenFunction" : null);
            var create = new TemplateInvocationRequest(output, parameters, name, DryRun: true);
            var effect = await support.InvokeAsync(templater, selected, binding, create, CancellationToken.None);
            effect.Should().BeNull("a missing corrected target must select ordinary native creation");
            var preview = await selected.InvokeAsync(create, CancellationToken.None);
            preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
            Directory.Exists(output.Info.FullName).Should().BeFalse();

            var created = await selected.InvokeAsync(create with { DryRun = false }, CancellationToken.None);

            created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
            File.Exists(app).Should().BeTrue("the approved native-only correction emits function_app.py");
            File.Exists(Path.Combine(output.Info.FullName, "function_app.py.py")).Should().BeFalse();
            Directory.GetFiles(output.Info.FullName).Should().ContainSingle().Which.Should().Be(app);
            mapping.CreateTargetFileName.Should().Be("function_app.py");
            mapping.AppendTargetFileName.Should().Be("function_app.py");
            var source = _sources.Single(item => item.Id == id);
            var legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy-content", explicitName ? "explicit" : "default"));
            var legacyParameters = new Dictionary<string, string?>(parameters) { ["trigger-functionName"] = name };
            new V2TemplateEngine().Apply(source, name, legacyParameters, legacy, false).Should().BeOfType<TemplateApplicationResult.Created>();
            File.ReadAllBytes(app).Should().Equal(File.ReadAllBytes(Directory.GetFiles(legacy.FullName).Single()),
                "only the native filename is corrected, not generated function-content bytes");
            var mappedSource = await selected.ReadPackageFileAsync(mapping.CreateSource.Identity, CancellationToken.None);
            Convert.ToHexStringLower(SHA256.HashData(mappedSource.Span)).Should().Be(mapping.CreateSource.Sha256);
            var mappedHelper = await selected.ReadPackageFileAsync(mapping.HelperSource.Identity, CancellationToken.None);
            Convert.ToHexStringLower(SHA256.HashData(mappedHelper.Span)).Should().Be(mapping.HelperSource.Sha256);

            File.AppendAllText(app, "\nimport user_dependency\n# retained user helper\ndef user_helper():\n    return 'retained'\n");
            var before = File.ReadAllBytes(app);
            var append = new TemplateInvocationRequest(output, parameters, "AddedFunction", DryRun: true);
            var appendPreview = await support.InvokeAsync(templater, selected, binding, append, CancellationToken.None);
            appendPreview!.Status.Should().Be(TemplateInvocationStatus.Success, appendPreview.Diagnostic);
            appendPreview.Changes.Should().BeEmpty();
            appendPreview.HasCommittedOutput.Should().BeFalse();
            appendPreview.Effects!.Kind.Should().Be("previewed");
            var plannedAppend = appendPreview.Effects.Plan.Effects.Should().ContainSingle().Subject
                .Should().BeOfType<TemplateAppendFileEffect>().Subject;
            plannedAppend.TargetPath.Should().Be(app);
            plannedAppend.Source.Should().Be(mapping.HelperSource);
            appendPreview.Effects.CompletedEffects.Should().BeEmpty();
            File.ReadAllBytes(app).Should().Equal(before);
            capability.Executions.Should().Be(0);

            var appended = await support.InvokeAsync(templater, selected, binding, append with { DryRun = false }, CancellationToken.None);

            appended!.Status.Should().Be(TemplateInvocationStatus.Success, appended.Diagnostic);
            appended.Effects!.Kind.Should().Be("applied");
            appended.Changes.Should().BeEmpty();
            appended.HasCommittedOutput.Should().BeFalse();
            appended.Effects.CompletedEffects.Should().ContainSingle().Which
                .Should().BeOfType<TemplateAppendFileEffect>().Which.Should().Be(plannedAppend);
            capability.Executions.Should().Be(1);
            File.ReadAllBytes(app).Take(before.Length).Should().Equal(before);
            File.ReadAllText(app).Should().Contain("AddedFunction", "def user_helper():");
            Directory.GetFiles(output.Info.FullName).Should().ContainSingle().Which.Should().Be(app);
        }
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("canceled")]
    [InlineData("declined")]
    public async Task FilenameCorrection_OccupiedCanceledOrDeclinedNativeTarget_IsNotOverwrittenOrRedirected(string state)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("EventGridTrigger-Python", TemplateType.Item)).Group!.Single();
        var mapping = await ReadAppendMappingAsync(selected);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "guarded"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        var sentinel = state == "directory" ? Path.Combine(Directory.CreateDirectory(app).FullName, "user.txt") : app;
        File.WriteAllText(sentinel, "user-owned bytes");
        using CancellationTokenSource cancellation = new();
        var capability = new OwnedPythonAppendCapability(selected, mapping.HelperSource, app)
        {
            BeforeValidation = state == "canceled" ? cancellation.Cancel : null,
        };
        var support = AppendSupport(mapping, capability, confirmed: state != "declined");
        var binding = await support.ResolveAsync(selected, cancellation.Token);
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "AddedFunction");

        if (state == "canceled")
        {
            Func<Task> invoke = () => support.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            await invoke.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await support.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
            if (state == "declined")
            {
                result.Effects!.Kind.Should().Be("declined");
            }
            else
            {
                result.Diagnostic.Should().Contain("existing directory");
            }
        }

        File.ReadAllText(sentinel).Should().Be("user-owned bytes");
        capability.Executions.Should().Be(0);
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py.py")).Should().BeFalse();
        Directory.GetFiles(output.Info.FullName, "*", SearchOption.AllDirectories).Should().ContainSingle().Which.Should().Be(sentinel);
    }

    [Fact]
    public void FilenameBeforeEvidence_DefaultV2CustomFilenameInput_IsNotNormalizedByNativeCorrection()
    {
        var source = _sources.Single(item => item.Id == "EventGridTrigger-Python");
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy-custom"));

        var result = new V2TemplateEngine().Apply(source, "Custom",
            new Dictionary<string, string?> { ["trigger-functionName"] = "Custom", ["app-fileName"] = "custom.py" }, legacy, false);

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        Directory.GetFiles(legacy.FullName).Should().ContainSingle().Which.Should().Be(Path.Combine(legacy.FullName, "custom.py.py"));
    }

    [Fact]
    public async Task FilenameCorrection_WithoutNativeOptIn_RemainsUnavailableAndDoesNotAlterDefaultV2()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"), enableBindingConstraints: false);

        var resolution = await templater.ResolveGroupAsync("EventGridTrigger-Python", TemplateType.Item);

        resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy-default"));
        new V2TemplateEngine().Apply(_sources.Single(source => source.Id == "EventGridTrigger-Python"), "LegacyFunction",
            new Dictionary<string, string?> { ["trigger-functionName"] = "LegacyFunction" }, legacy, false)
            .Should().BeOfType<TemplateApplicationResult.Created>();
        Directory.GetFiles(legacy.FullName).Should().ContainSingle().Which.Should().Be(Path.Combine(legacy.FullName, "function_app.py.py"));
    }

    private async Task<string> ResolveFilenameNameAsync(ResolvedTemplate selected, string? requestedName, CancellationToken cancellationToken = default)
    {
        PythonItemCompatibility compatibility = new(Substitute.For<IInteractionService>(), new PythonTemplateSourceReader());
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []), cancellationToken);
        var result = await compatibility.ResolveAsync(selected, arguments, requestedName, false, cancellationToken);
        result.Succeeded.Should().BeTrue(string.Join(" ", result.Diagnostics));
        result.Origin.Should().Be(requestedName is null ? "authoredDefault" : "explicit");
        return result.Name!;
    }
}
