// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Theory]
    [InlineData("HttpTrigger-Python")]
    [InlineData("TimerTrigger-Python")]
    public async Task AppFilename_NativePreviewAndCreate_UsesExactSourceOwnedFilenameAndContent(string id)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app.py"]));
        parsed.IsComplete.Should().BeTrue();
        var provider = CreatePythonProvider();
        var name = await provider.Compatibility.ResolveAsync(selected, parsed, "ParityCustomPy", false, CancellationToken.None);
        name.Succeeded.Should().BeTrue();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "custom-app"));
        var request = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, name.Name, DryRun: true);

        var preview = await provider.Effects.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        preview!.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        var change = preview.Changes.Should().ContainSingle().Subject;
        NativeTemplateSourceSnapshot.Normalize(change.Path).Should().Be("parity_app.py");
        change.Kind.Should().Be("Create");
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await provider.Effects.InvokeAsync(templater, selected, binding, request with { DryRun = false }, CancellationToken.None);

        created!.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        var raw = _sources.Single(source => source.Id == id);
        string expected = raw.Files!["function_app.py"].Replace("$(FUNCTION_NAME_INPUT)", "ParityCustomPy", StringComparison.Ordinal);
        foreach (var input in raw.Jobs!.Single(job => job.Type == "CreateNewApp").Inputs!)
        {
            if (input.AssignTo is not null && input.ParamId is not null
                && parsed.ValuesByCanonicalName.TryGetValue(input.ParamId, out string? value) && value is not null)
            {
                expected = expected.Replace(input.AssignTo, value, StringComparison.Ordinal);
            }
        }
        File.ReadAllBytes(Path.Combine(output.Info.FullName, "parity_app.py")).Should().Equal(Encoding.UTF8.GetBytes(expected));
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py")).Should().BeFalse();
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppFilename_OccupiedCustomTarget_IsNeverReplacedByCreateOnlyCapability(bool force)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app.py"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "occupied-app"));
        Directory.CreateDirectory(output.Info.FullName);
        string target = Path.Combine(output.Info.FullName, "parity_app.py");
        File.WriteAllText(target, "user-owned blueprint or app");

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten", Force: force), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.ReadAllText(target).Should().Be("user-owned blueprint or app");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Fact]
    public async Task AppFilename_ExistingDefaultApp_RetainsTheQualifiedAppendTargetAndUserCode()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", "unused_create_name.py"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "default-app"));
        Directory.CreateDirectory(output.Info.FullName);
        string target = Path.Combine(output.Info.FullName, "function_app.py");
        const string original = "import azure.functions as func\napp = func.FunctionApp()\n# user-owned\n";
        File.WriteAllText(target, original);

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Appended"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        result.Changes.Should().Equal(new TemplateFileChange("function_app.py", "Append"));
        File.ReadAllText(target).Should().StartWith(original).And.Contain("def Appended(");
        File.Exists(Path.Combine(output.Info.FullName, "unused_create_name.py")).Should().BeFalse();
    }

    [Fact]
    public async Task AppFilename_UnplannedNativeOutput_RefusesBeforeAnyFilesAreWritten()
    {
        using (var archive = ZipFile.Open(_archive, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("content/items/HttpTrigger/unplanned.py").Open());
            writer.Write("not part of the selected create-source plan");
        }
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app.py"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "unplanned-output"));

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppFilename_CancelOrCollisionAfterNativeCapture_DoesNotWriteAnUnplannedTarget(bool cancel)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var original = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(original.Entry.Identity, original.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "capture-boundary"));
        string target = Path.Combine(output.Info.FullName, "parity_app.py");
        using CancellationTokenSource cancellation = new();
        ResolvedTemplate selected = new(original.Entry, original.InvokeAsync, original.ReadPackageFileAsync,
            original.ConfigurationSha256, original.ValidateConfigurationAsync, original.ConfigurationSourceIdentity,
            async (request, files, expand, token) =>
            {
                var result = await original.CaptureNativePreparationAsync(request, files, expand, token);
                if (cancel)
                {
                    cancellation.Cancel();
                }
                else
                {
                    Directory.CreateDirectory(output.Info.FullName);
                    File.WriteAllText(target, "new user file after capture");
                }
                return result;
            });
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, cancellation.Token);
        var invocation = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, "Unwritten");

        if (cancel)
        {
            Func<Task> invoke = () => provider.Effects.InvokeAsync(templater, selected, binding, invocation, cancellation.Token);
            await invoke.Should().ThrowAsync<OperationCanceledException>();
            Directory.Exists(output.Info.FullName).Should().BeFalse();
        }
        else
        {
            var result = await provider.Effects.InvokeAsync(templater, selected, binding, invocation, cancellation.Token);
            result!.Status.Should().Be(TemplateInvocationStatus.FileConflict);
            File.ReadAllText(target).Should().Be("new user file after capture");
            Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
        }
    }

    [Theory]
    [InlineData("parity_app.py", true)]
    [InlineData("another_app.PY", true)]
    [InlineData("../outside.py", false)]
    [InlineData("nested/app.py", false)]
    [InlineData("nested\\app.py", false)]
    [InlineData("C:\\outside.py", false)]
    [InlineData("bad.py:stream", false)]
    [InlineData("app.py ", false)]
    [InlineData("CON.py", false)]
    [InlineData("LPT1.py", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AppFilename_QualifiedCreateNames_RespectPortablePathBoundary(string? filename, bool permitted)
        => (PythonAppFileName.GetDiagnostic(filename) is null).Should().Be(permitted);
}
