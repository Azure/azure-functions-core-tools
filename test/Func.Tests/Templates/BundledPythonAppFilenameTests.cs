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
    [InlineData("HttpTrigger-Python", "parity_app.py", "ParityCustomPy")]
    [InlineData("TimerTrigger-Python", "parity_app.py", "ParityCustomPy")]
    [InlineData("HttpTrigger-Python", "parity_app", "ParityCustomApp")]
    [InlineData("TimerTrigger-Python", "parity_app", "ParityCustomApp")]
    public async Task AppFilename_NativePreviewAndCreate_UsesExactSourceOwnedFilenameAndContent(string id, string filename, string functionName)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", filename]));
        parsed.IsComplete.Should().BeTrue();
        var provider = CreatePythonProvider();
        var name = await provider.Compatibility.ResolveAsync(selected, parsed, functionName, false, CancellationToken.None);
        name.Succeeded.Should().BeTrue();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "custom-app"));
        var request = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, name.Name, DryRun: true);

        var preview = await provider.Effects.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        preview!.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        var change = preview.Changes.Should().ContainSingle().Subject;
        NativeTemplateSourceSnapshot.Normalize(change.Path).Should().Be(filename);
        change.Kind.Should().Be("Create");
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await provider.Effects.InvokeAsync(templater, selected, binding, request with { DryRun = false }, CancellationToken.None);

        created!.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        var raw = _sources.Single(source => source.Id == id);
        string expected = raw.Files!["function_app.py"].Replace("$(FUNCTION_NAME_INPUT)", functionName, StringComparison.Ordinal);
        foreach (var input in raw.Jobs!.Single(job => job.Type == "CreateNewApp").Inputs!)
        {
            if (input.AssignTo is not null && input.ParamId is not null
                && parsed.ValuesByCanonicalName.TryGetValue(input.ParamId, out string? value) && value is not null)
            {
                expected = expected.Replace(input.AssignTo, value, StringComparison.Ordinal);
            }
        }
        File.ReadAllBytes(Path.Combine(output.Info.FullName, filename)).Should().Equal(Encoding.UTF8.GetBytes(expected));
        if (id == "HttpTrigger-Python" && filename == "parity_app")
        {
            byte[] actual = File.ReadAllBytes(Path.Combine(output.Info.FullName, filename));
            actual.Should().HaveCount(860);
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actual)).Should()
                .Be("28998735583802BCD72E1B235D01EC516B90E1F9974B3C99AAD91424BEA7EC20");
        }
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py")).Should().BeFalse();
        if (!filename.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
            File.Exists(Path.Combine(output.Info.FullName, filename + ".py")).Should().BeFalse();
        }
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, "parity_app.py", false)]
    [InlineData(true, "parity_app.py", false)]
    [InlineData(false, "parity_app", false)]
    [InlineData(true, "parity_app", false)]
    [InlineData(false, "parity_app", true)]
    [InlineData(true, "parity_app", true)]
    public async Task AppFilename_OccupiedCustomTarget_IsNeverReplacedByCreateOnlyCapability(bool force, string filename, bool directory)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", filename]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "occupied-app"));
        Directory.CreateDirectory(output.Info.FullName);
        string target = Path.Combine(output.Info.FullName, filename);
        if (directory)
        {
            Directory.CreateDirectory(target);
        }
        else
        {
            File.WriteAllText(target, "user-owned blueprint or app");
        }

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten", Force: force), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        if (directory)
        {
            Directory.Exists(target).Should().BeTrue();
            Directory.GetFileSystemEntries(target).Should().BeEmpty();
        }
        else
        {
            File.ReadAllText(target).Should().Be("user-owned blueprint or app");
        }
        Directory.GetFileSystemEntries(output.Info.FullName).Should().ContainSingle();
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
    public async Task AppFilename_ExtensionlessWithExistingDefaultApp_DoesNotBroadenAppendAdmission()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing-default-extensionless"));
        Directory.CreateDirectory(output.Info.FullName);
        string target = Path.Combine(output.Info.FullName, "function_app.py");
        const string original = "import azure.functions as func\napp = func.FunctionApp()\n# unchanged user code\n";
        File.WriteAllText(target, original);

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        File.ReadAllText(target).Should().Be(original);
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Fact]
    public async Task AppFilename_ExtensionlessForceOnMissingTarget_RefusesWithoutCreatingOutput()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "missing-force-target"));

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten", Force: true), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("parity_app.py")]
    [InlineData("parity_app")]
    public async Task AppFilename_UnplannedNativeOutput_RefusesBeforeAnyFilesAreWritten(string filename)
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
            ["--app-file-name", filename]));
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
    public async Task AppFilename_CanonicalTargetAppearsAfterCapture_RefusesWithoutCreatingOrAppending(bool directory)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var original = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(original.Entry.Identity, original.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canonical-after-capture"));
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        ResolvedTemplate selected = new(original.Entry, original.InvokeAsync, original.ReadPackageFileAsync,
            original.ConfigurationSha256, original.ValidateConfigurationAsync, original.ConfigurationSourceIdentity,
            async (request, files, expand, token) =>
            {
                var result = await original.CaptureNativePreparationAsync(request, files, expand, token);
                Directory.CreateDirectory(output.Info.FullName);
                if (directory)
                {
                    Directory.CreateDirectory(canonical);
                }
                else
                {
                    File.WriteAllText(canonical, "new user canonical app");
                }
                return result;
            });
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.Exists(Path.Combine(output.Info.FullName, "parity_app")).Should().BeFalse();
        if (directory)
        {
            Directory.GetFileSystemEntries(canonical).Should().BeEmpty();
        }
        else
        {
            File.ReadAllText(canonical).Should().Be("new user canonical app");
        }
        Directory.GetFileSystemEntries(output.Info.FullName).Should().ContainSingle();
    }

    [Fact]
    public async Task AppFilename_SuppliedSourcePlan_CannotBeReplacedToEnableAnExtensionlessWrite()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-file-name", "parity_app"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "supplied-source-plan"));
        var request = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, "Unwritten")
        {
            SourceSnapshot = new(selected.Entry.Identity, selected.ConfigurationSha256!,
                [binding.Append!.MappingSource, binding.Append.CreateSource],
                [new(binding.Append.CreateTargetFileName, binding.Append.CreateSource)]),
        };

        var result = await provider.Effects.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, "parity_app.py")]
    [InlineData(true, "parity_app.py")]
    [InlineData(false, "parity_app")]
    [InlineData(true, "parity_app")]
    public async Task AppFilename_CancelOrCollisionAfterNativeCapture_DoesNotWriteAnUnplannedTarget(bool cancel, string filename)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var original = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(original.Entry.Identity, original.Parameters, new HashSet<string>(),
            ["--app-file-name", filename]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "capture-boundary"));
        string target = Path.Combine(output.Info.FullName, filename);
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

    [Theory]
    [InlineData("parity_app", true)]
    [InlineData("safe-app_2", true)]
    [InlineData("parity_app.py", true)]
    [InlineData("parity_app.txt", false)]
    [InlineData(".func", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("../outside", false)]
    [InlineData("nested/app", false)]
    [InlineData("nested\\app", false)]
    [InlineData("C:app", false)]
    [InlineData("app:stream", false)]
    [InlineData("app ", false)]
    [InlineData("app.", false)]
    [InlineData("app name", false)]
    [InlineData("CON", false)]
    [InlineData("LPT1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AppFilename_ExtensionlessCreateNames_RetainPortableAndReservedBoundaries(string? filename, bool permitted)
        => (PythonAppFileName.GetDiagnostic(filename, allowExtensionless: true) is null).Should().Be(permitted);
}
