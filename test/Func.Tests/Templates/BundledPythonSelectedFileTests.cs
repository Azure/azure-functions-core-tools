// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;
using Azure.Functions.Cli.Templates.Python.Shared;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Microsoft.Win32.SafeHandles;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    private const string SelectedAppPrefix =
        "import datetime\r\nimport json\r\nimport logging\r\n\r\nimport azure.functions as func\r\n\r\n"
        + "app = func.FunctionApp(http_auth_level=func.AuthLevel.FUNCTION)\r\n";

    [Fact]
    public async Task SelectedFile_DeclaredAlias_PreviewsAndAppendsOnlyTheSelectedExistingApp()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        parsed.IsComplete.Should().BeTrue();
        parsed.ValuesByCanonicalName["app-selectedFileName"].Should().Be("selected_app.py");
        var help = new NativeTemplateHelpProjector(new TemplateArgumentParser()).Project(selected.Entry, new HashSet<string>(), CancellationToken.None);
        help.HydratedOptions.Should().Contain(option => option.Name == "--app-selected-file-name");
        var provider = CreatePythonProvider();
        var resolved = await provider.Compatibility.ResolveAsync(selected, parsed, null, false, CancellationToken.None);
        resolved.Name.Should().Be("http_trigger");
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "selected-app"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        new FileInfo(canonical).Length.Should().Be(147);
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(canonical))).Should()
            .Be("4B313AE0CB052E861C2E0480DA84BA6E0C1C9E93A0BBED35EBAAA49172AFFB84");
        var request = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, resolved.Name, DryRun: true);

        var preview = await provider.Effects.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        preview!.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Changes.Should().Equal(new TemplateFileChange("selected_app.py", TemplateFileChangeKind.Append));
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);

        var applied = await provider.Effects.InvokeAsync(templater, selected, binding, request with { DryRun = false }, CancellationToken.None);

        applied!.Status.Should().Be(TemplateInvocationStatus.Success, applied.Diagnostic);
        File.ReadAllBytes(canonical).Should().Equal(Encoding.UTF8.GetBytes(SelectedAppPrefix));
        byte[] actual = File.ReadAllBytes(target);
        actual.Should().HaveCount(920);
        Convert.ToHexString(SHA256.HashData(actual)).Should()
            .Be("E3BA87762CD4915DEE7B918199D6117BE69D0E047664A01F1471A445DAEDD35D");
        Directory.GetFiles(output.Info.FullName).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedFile_OmittedAndExplicitCanonicalValues_RetainCanonicalAppend(bool explicitCanonical)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        string[] tokens = explicitCanonical ? ["--app-selected-file-name", "function_app.py"] : [];
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), tokens));
        parsed.ValuesByCanonicalName["app-selectedFileName"].Should().Be("function_app.py");
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canonical-selection"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string alternate = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(alternate, SelectedAppPrefix);

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "CanonicalOnly"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        result.Changes.Should().Equal(new TemplateFileChange("function_app.py", TemplateFileChangeKind.Append));
        File.ReadAllText(canonical).Should().Contain("def CanonicalOnly(");
        File.ReadAllText(alternate).Should().Be(SelectedAppPrefix);
    }

    [Theory]
    [InlineData("selected-missing")]
    [InlineData("selected-directory")]
    [InlineData("canonical-missing")]
    [InlineData("canonical-directory")]
    [InlineData("")]
    [InlineData("../outside.py")]
    [InlineData("nested/app.py")]
    [InlineData("C:\\outside.py")]
    [InlineData("bad.py:stream")]
    [InlineData("CON.py")]
    [InlineData(".func")]
    [InlineData("selected_app")]
    [InlineData("selected.app.py")]
    [InlineData("\u03c0.py")]
    public async Task SelectedFile_InvalidTarget_NeverCreatesOrFallsBackToCanonical(string scenario)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        string name = scenario.Contains('-') ? "selected_app.py" : scenario;
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", name]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "invalid-selection"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        if (scenario == "canonical-directory") Directory.CreateDirectory(canonical);
        else if (scenario != "canonical-missing") File.WriteAllText(canonical, SelectedAppPrefix);
        if (scenario == "selected-directory") Directory.CreateDirectory(target);
        else if (scenario != "selected-missing") File.WriteAllText(target, SelectedAppPrefix);
        string[] before = Directory.GetFileSystemEntries(output.Info.FullName);

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "MustNotWrite"), CancellationToken.None);

        result.Should().NotBeNull("a failed nondefault selection must not enter ordinary creation");
        result!.Status.Should().NotBe(TemplateInvocationStatus.Success);
        Directory.GetFileSystemEntries(output.Info.FullName).Should().Equal(before);
        if (File.Exists(canonical)) File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        if (File.Exists(target)) File.ReadAllText(target).Should().Be(SelectedAppPrefix);
        File.Exists(Path.Combine(_root, "outside.py")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedFile_DuplicateFunction_RefusesEvenWithForceWithoutChangingCanonical(bool force)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "selected-collision"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        const string duplicate = SelectedAppPrefix + "@app.route(route='existing')\ndef Duplicated(req):\n    pass\n";
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, duplicate);

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Duplicated", Force: force), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Be(duplicate);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("canonical-gone")]
    public async Task SelectedFile_StateChangesAfterNativeExpansion_CannotRedirectOrAppend(string scenario)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "selected-state-change"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        using CancellationTokenSource cancellation = new();
        bool expanded = false;
        var files = SelectedFileSystem(path =>
        {
            if (Path.GetFileName(path) != "function_body.py") return;
            expanded = true;
            if (scenario == "cancel") cancellation.Cancel();
            else if (scenario == "canonical-gone") File.Delete(canonical);
            else File.WriteAllText(target, "new user edits");
        });
        var effects = new PythonSelectedItemEffects(files, new(), new PythonTemplateAppendExecutor(),
            confirmed: true, identities: new PlatformFileIdentityReader());
        var binding = await effects.ResolveAsync(selected, cancellation.Token);
        var request = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, "Unwritten");

        if (scenario == "cancel")
        {
            Func<Task> invoke = () => effects.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            await invoke.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await effects.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        }
        expanded.Should().BeTrue();
        File.ReadAllText(target).Should().Be(scenario == "edit" ? "new user edits" : SelectedAppPrefix);
        if (scenario == "canonical-gone") File.Exists(canonical).Should().BeFalse("failed selection must not create a replacement");
        else File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
    }

    [Fact]
    public async Task SelectedFile_Binding_IsPerInvocationAndRejectsSourceDriftOrWrongEffectTarget()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var source = await new PythonTemplateSourceReader().ReadAsync(selected, CancellationToken.None);
        var values = new Dictionary<string, string?> { ["app-selectedFileName"] = "selected_app.py" };
        var captured = PythonAppendTargetSelection.Capture(source.Binding.Append!, values);
        values["app-selectedFileName"] = "another.py";
        captured.FileName.Should().Be("selected_app.py");
        captured.Bind(source.Binding.Append!).AppendTargetFileName.Should().Be("selected_app.py");
        Action drift = () => captured.Bind(source.Binding.Append! with { HelperIdentity = "Foreign.Helper" });
        drift.Should().Throw<InvalidDataException>();
        Action missingParameters = () => PythonAppendTargetSelection.Capture(source.Binding.Append!, null!);
        Action missingCurrent = () => captured.Bind(null!);
        missingParameters.Should().Throw<ArgumentNullException>();
        missingCurrent.Should().Throw<ArgumentNullException>();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "bound-target"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        var executor = Substitute.For<IPythonTemplateAppendExecutor>();
        var capability = new PythonAppendCapability(new PhysicalFileSystem(), new(), executor, selected, output, selection: captured);
        var wrong = new TemplateAppendFileEffect(source.HelperSource, canonical, "must not append",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(canonical))));

        var result = await capability.ExecuteAsync(wrong, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        executor.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
    }

    [Fact]
    public async Task SelectedFile_SuppliedSourcePlan_RefusesInsteadOfReplacingItsAuthority()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var mapping = binding.Append!;
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "selected-source-plan"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        var request = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, "Unwritten")
        {
            SourceSnapshot = new(selected.Entry.Identity, selected.ConfigurationSha256!,
                [mapping.MappingSource, mapping.CreateSource], [new(mapping.CreateTargetFileName, mapping.CreateSource)]),
        };

        var result = await provider.Effects.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
    }

    [Fact]
    public async Task SelectedFile_CallerParameterMutationAfterExpansion_DoesNotRedirectTheCapturedTarget()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var values = selected.Parameters.ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue, StringComparer.Ordinal);
        values["app-selectedFileName"] = "selected_app.py";
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "selector-mutation"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        bool expanded = false;
        var files = SelectedFileSystem(path =>
        {
            if (Path.GetFileName(path) != "function_body.py") return;
            expanded = true;
            values["app-selectedFileName"] = "function_app.py";
        });
        var effects = new PythonSelectedItemEffects(files, new(), new PythonTemplateAppendExecutor(),
            confirmed: true, identities: new PlatformFileIdentityReader());
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);

        var result = await effects.InvokeAsync(templater, selected, binding, new(output, values, "Captured"), CancellationToken.None);

        expanded.Should().BeTrue();
        result!.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        result.Changes.Should().Equal(new TemplateFileChange("selected_app.py", TemplateFileChangeKind.Append));
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Contain("def Captured(");
    }

    [SymbolicLinkFact]
    public async Task SelectedFile_LinkedExistingTarget_CannotBorrowCanonicalAppAppendPermission()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "selected-link"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        string outside = Path.Combine(_root, "external-app.py");
        File.WriteAllText(outside, SelectedAppPrefix);
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.CreateSymbolicLink(target, outside);
        try
        {
            var result = await provider.Effects.InvokeAsync(templater, selected, binding,
                new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

            result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
            File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
            File.ReadAllText(outside).Should().Be(SelectedAppPrefix);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("protected")]
    [InlineData("outside")]
    public async Task SelectedFile_HardLinkedToCanonical_RefusesWithoutChangingEitherApp(string alias)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var provider = CreatePythonProvider();
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "selected-hard-link"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        string linked = alias == "canonical" ? canonical
            : Path.Combine(alias == "outside" ? _root : output.Info.FullName, "protected-app.py");
        if (alias != "canonical") File.WriteAllText(linked, SelectedAppPrefix);
        await CreateSelectedHardLinkAsync(target, linked, CancellationToken.None);
        PlatformFileIdentityReader identities = new();
        identities.Read(target).LinkCount.Should().Be(2);
        identities.Read(target).Identifies(identities.Read(linked)).Should().BeTrue();

        var result = await provider.Effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix, "a different pathname can share the same hard-linked file");
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
        File.ReadAllText(linked).Should().Be(SelectedAppPrefix);
        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
    }

    [Fact]
    public async Task SelectedFile_SameContentIdentityReplacementAfterExpansion_RefusesBeforeAppend()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "identity-replacement"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        string backup = Path.Combine(output.Info.FullName, "old-app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        bool expanded = false;
        var files = SelectedFileSystem(path =>
        {
            if (Path.GetFileName(path) != "function_body.py") return;
            expanded = true;
            File.Move(target, backup);
            File.Copy(backup, target);
        });
        var effects = new PythonSelectedItemEffects(files, new(), new PythonTemplateAppendExecutor(),
            confirmed: true, identities: new PlatformFileIdentityReader());
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);

        var result = await effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        expanded.Should().BeTrue();
        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        File.ReadAllBytes(backup).Should().Equal(File.ReadAllBytes(target), "equal SHA-256 content cannot prove object identity");
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("held-identity")]
    [InlineData("multiple-links")]
    [InlineData("missing-fields")]
    [InlineData("path-drift")]
    [InlineData("unsupported")]
    public async Task SelectedFile_ActualHeldWriteHandle_IsValidatedBeforeBytesAreAppended(string scenario)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "held-target"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        PlatformFileIdentityReader physical = new();
        var identities = Substitute.For<IFileIdentityReader>();
        int heldReads = 0;
        identities.Read(Arg.Any<string>()).Returns(call =>
        {
            string path = call.Arg<string>();
            FileIdentity actual = physical.Read(path);
            return scenario == "path-drift" && heldReads > 0 && path == target ? actual with { Low = actual.Low ^ 1 } : actual;
        });
        identities.Read(Arg.Any<SafeFileHandle>()).Returns(call =>
        {
            heldReads++;
            if (scenario == "unsupported") throw new InvalidDataException("Identity API unavailable.");
            FileIdentity actual = physical.Read(call.Arg<SafeFileHandle>());
            return scenario switch
            {
                "held-identity" => actual with { Low = actual.Low ^ 1 },
                "multiple-links" => actual with { LinkCount = 2 },
                "missing-fields" => default,
                _ => actual,
            };
        });
        var effects = new PythonSelectedItemEffects(new PhysicalFileSystem(), new(), new PythonTemplateAppendExecutor(),
            confirmed: true, identities: identities);
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);

        var result = await effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "HeldTarget"), CancellationToken.None);

        heldReads.Should().BeGreaterThan(0, "the selected executor must validate its actual open write handle");
        result!.Status.Should().Be(scenario == "valid" ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EffectFailure);
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        if (scenario == "valid")
        {
            heldReads.Should().Be(2, "held target must be checked after opening and immediately before writing");
            File.ReadAllText(target).Should().Contain("def HeldTarget(");
        }
        else File.ReadAllText(target).Should().Be(SelectedAppPrefix);
    }

    [Fact]
    public async Task SelectedFile_OldExecutorShape_PreservesCanonicalDispatchAndExplicitlyRefusesSelectedWrites()
    {
        IPythonTemplateAppendExecutor executor = new OldShapeAppendExecutor();
        await executor.AppendAsync("canonical", "snippet", "digest", CancellationToken.None);
        Func<Task> selected = () => executor.AppendSelectedAsync("alternate", "snippet", "digest", _ => { }, CancellationToken.None);

        await selected.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not support held-target identity*");
        ((OldShapeAppendExecutor)executor).Calls.Should().Be(1);
    }

    [Fact]
    public async Task SelectedFile_MissingHeldHandleValidator_CannotAuthorizeAnAppend()
    {
        var executor = new PythonTemplateAppendExecutor();
        string target = Path.Combine(_root, "missing-held-guard.py");
        File.WriteAllText(target, SelectedAppPrefix);
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));

        Func<Task> invoke = () => executor.AppendSelectedAsync(target, "must not append", digest, null!, CancellationToken.None);

        await invoke.Should().ThrowAsync<ArgumentNullException>();
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
    }

    [Fact]
    public async Task SelectedFile_NonregularMetadata_RefusesBeforeAnyDigestReadOrNativeExpansion()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "nonregular-target"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        var files = SelectedFileSystem(_ => throw new InvalidOperationException("No helper or target content may be read."));
        var identities = Substitute.For<IFileIdentityReader>();
        identities.Read(target).Returns(new FileIdentity(1, 2, 0, 1, IsRegular: false));
        var effects = new PythonSelectedItemEffects(files, new(), new PythonTemplateAppendExecutor(), confirmed: true, identities: identities);
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);

        var result = await effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        await files.DidNotReceive().ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>());
        files.DidNotReceive().CreateTempDirectory();
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("missing")]
    [InlineData("io")]
    public async Task SelectedFile_TargetDigestReadFailure_ProducesStructuredRefusalWithoutFallback(string failure)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "unreadable-target"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        var files = SelectedFileSystem(_ => throw new InvalidOperationException("No private expansion may precede the target digest."));
        files.ComputeSha256Async(target, Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromException<string>(failure switch
            {
                "permission" => new UnauthorizedAccessException("Selected app contents are not readable."),
                "missing" => new FileNotFoundException("Selected app disappeared.", target),
                _ => new IOException("Selected app read failed."),
            }));
        var effects = new PythonSelectedItemEffects(files, new(), new PythonTemplateAppendExecutor(),
            confirmed: true, identities: new PlatformFileIdentityReader());
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);

        var result = await effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Diagnostic.Should().NotBeNullOrWhiteSpace();
        files.DidNotReceive().CreateTempDirectory();
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
    }

    [Fact]
    public async Task SelectedFile_TargetDigestCancellation_PropagatesTheExactInvocationToken()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "cancel-digest"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        using CancellationTokenSource cancellation = new();
        var files = SelectedFileSystem(_ => throw new InvalidOperationException("No expansion is allowed after cancellation."));
        bool digestReached = false;
        files.ComputeSha256Async(target, Arg.Any<CancellationToken>()).Returns(call =>
        {
            digestReached = true;
            CancellationToken token = call.Arg<CancellationToken>();
            token.Should().Be(cancellation.Token);
            cancellation.Cancel();
            return Task.FromCanceled<string>(token);
        });
        var effects = new PythonSelectedItemEffects(files, new(), new PythonTemplateAppendExecutor(),
            confirmed: true, identities: new PlatformFileIdentityReader());
        var binding = await effects.ResolveAsync(selected, cancellation.Token);

        Func<Task> invoke = () => effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), cancellation.Token);

        var thrown = await invoke.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.CancellationToken.Should().Be(cancellation.Token);
        digestReached.Should().BeTrue();
        files.DidNotReceive().CreateTempDirectory();
        File.ReadAllText(canonical).Should().Be(SelectedAppPrefix);
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
    }

    private sealed class OldShapeAppendExecutor : IPythonTemplateAppendExecutor
    {
        public int Calls { get; private set; }

        public Task AppendAsync(string absoluteTargetPath, string generatedSnippet, string expectedTargetSha256, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static async Task CreateSelectedHardLinkAsync(string target, string source, CancellationToken cancellationToken)
    {
        string command = $"New-Item -ItemType HardLink -Path '{target.Replace("'", "''", StringComparison.Ordinal)}' "
            + $"-Target '{source.Replace("'", "''", StringComparison.Ordinal)}' -ErrorAction Stop | Out-Null";
        ProcessStartInfo start = new("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        (await stdout).Should().BeEmpty();
        process.ExitCode.Should().Be(0, await stderr);
    }

    [Theory]
    [InlineData("HttpTrigger-Python")]
    [InlineData("ServiceBusTopicTrigger-Python")]
    public async Task SelectedFile_CanonicalDisappearsBeforeSharedSupport_CannotFallBackToCreation(string id)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--app-selected-file-name", "selected_app.py"]));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canonical-disappears"));
        Directory.CreateDirectory(output.Info.FullName);
        string canonical = Path.Combine(output.Info.FullName, "function_app.py");
        string target = Path.Combine(output.Info.FullName, "selected_app.py");
        File.WriteAllText(canonical, SelectedAppPrefix);
        File.WriteAllText(target, SelectedAppPrefix);
        var files = SelectedFileSystem(_ => { });
        PhysicalFileSystem physical = new();
        int canonicalReads = 0;
        files.FileExists(Arg.Any<string>()).Returns(call =>
        {
            string path = call.Arg<string>();
            if (path == canonical && ++canonicalReads == 2) File.Delete(canonical);
            return physical.FileExists(path);
        });
        var effects = new PythonSelectedItemEffects(files, new(), new PythonTemplateAppendExecutor(),
            confirmed: true, identities: new PlatformFileIdentityReader());
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);

        var result = await effects.InvokeAsync(templater, selected, binding,
            new(output, parsed.ValuesByCanonicalName, "Unwritten"), CancellationToken.None);

        result.Should().NotBeNull("missing canonical app must not return the normal-create sentinel for alternate selection");
        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        File.Exists(canonical).Should().BeFalse();
        File.ReadAllText(target).Should().Be(SelectedAppPrefix);
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("default")]
    [InlineData("alias")]
    [InlineData("rename")]
    public async Task SelectedFile_Metadata_IsSourceQualifiedAndOldPackagesDoNotAcquireTheAlias(string scenario)
    {
        using (var zip = ZipFile.Open(_archive, ZipArchiveMode.Update))
        {
            JsonNode manifest = ReadNode("source/applications.json");
            JsonNode application = manifest["applications"]!.AsArray().Single(item =>
                item!["templateIdentity"]!.GetValue<string>() == "Azure.Functions.Python.HttpTrigger")!;
            foreach (var item in new[] { application, application["appendHelper"]! })
            {
                string path = item["configuration"]!.GetValue<string>();
                JsonNode config = ReadNode(path);
                if (scenario == "absent") config["symbols"]!.AsObject().Remove("app-selectedFileName");
                else if (scenario == "default") config["symbols"]!["app-selectedFileName"]!["defaultValue"] = "foreign.py";
                else if (scenario == "rename") config["symbols"]!["app-selectedFileName"]!["fileRename"] = "function_app.py";
                item["configurationSha256"] = WriteNode(path, config);
                string hostPath = path.Replace("template.json", "func.host.json", StringComparison.Ordinal);
                JsonNode host = ReadNode(hostPath);
                if (scenario == "absent") host["symbolInfo"]!.AsObject().Remove("app-selectedFileName");
                else if (scenario == "alias") host["symbolInfo"]!["app-selectedFileName"]!["longName"] = "foreign-selector";
                WriteNode(hostPath, host);
            }
            WriteNode("source/applications.json", manifest);

            JsonNode ReadNode(string path)
            {
                using var reader = new StreamReader(zip.GetEntry(path)!.Open());
                return JsonNode.Parse(reader.ReadToEnd())!;
            }

            string WriteNode(string path, JsonNode node)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
                zip.GetEntry(path)!.Delete();
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(bytes);
                return Convert.ToHexStringLower(SHA256.HashData(bytes));
            }
        }
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var provider = CreatePythonProvider();

        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);

        if (scenario == "absent")
        {
            binding.Diagnostics.Should().BeEmpty();
            binding.Append!.SelectedAppendFileNameParameter.Should().BeNull();
            var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
                ["--app-selected-file-name", "selected_app.py"]));
            parsed.IsComplete.Should().BeFalse("old markerless metadata must not acquire an undeclared alias");
            var omitted = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "older-package"));
            Directory.CreateDirectory(output.Info.FullName);
            string canonical = Path.Combine(output.Info.FullName, "function_app.py");
            string target = Path.Combine(output.Info.FullName, "selected_app.py");
            File.WriteAllText(canonical, SelectedAppPrefix);
            File.WriteAllText(target, SelectedAppPrefix);
            var appended = await provider.Effects.InvokeAsync(templater, selected, binding,
                new(output, omitted.ValuesByCanonicalName, "OldDefault"), CancellationToken.None);
            appended!.Status.Should().Be(TemplateInvocationStatus.Success, appended.Diagnostic);
            File.ReadAllText(canonical).Should().Contain("def OldDefault(");
            File.ReadAllText(target).Should().Be(SelectedAppPrefix);
        }
        else
        {
            binding.Append.Should().BeNull();
            binding.Diagnostics.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void SelectedFile_BindingNullContracts_AreProgrammerErrors()
    {
        Action missingMapping = () => PythonAppendTargetSelection.Capture(null!, new Dictionary<string, string?>());
        missingMapping.Should().Throw<ArgumentNullException>();
    }

    private static IFileSystem SelectedFileSystem(Action<string> afterRead)
    {
        PhysicalFileSystem physical = new();
        var files = Substitute.For<IFileSystem>();
        files.FileExists(Arg.Any<string>()).Returns(call => physical.FileExists(call.Arg<string>()));
        files.DirectoryExists(Arg.Any<string>()).Returns(call => physical.DirectoryExists(call.Arg<string>()));
        files.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.ComputeSha256Async(call.Arg<string>(), call.Arg<CancellationToken>()));
        files.CreateTempDirectory().Returns(_ => physical.CreateTempDirectory());
        files.GetFileSystemEntries(Arg.Any<string>()).Returns(call => physical.GetFileSystemEntries(call.Arg<string>()));
        files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            string path = call.Arg<string>();
            string? content = await physical.ReadAllTextIfExistsAsync(path, call.Arg<CancellationToken>());
            afterRead(path);
            return content;
        });
        return files;
    }
}
