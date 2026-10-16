// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;
using Azure.Functions.Cli.Templates.Python.Shared;
using Azure.Functions.Cli.Tests.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Theory]
    [InlineData("stable", 18)]
    [InlineData("preview", 24)]
    [InlineData("experimental", 23)]
    public async Task PythonProvider_AllEligibleOriginalsAndHelpers_AreQualifiedFromProductionSourceReader(string channel, int expected)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync(channel));
        var entries = await templater.ListAsync(TemplateType.Item);
        var reader = new PythonTemplateSourceReader();
        var eligible = entries.Where(entry => entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible).ToList();
        eligible.Should().HaveCount(expected * 2);
        foreach (var entry in eligible)
        {
            var selected = (await templater.ResolveGroupAsync(entry.Identity, TemplateType.Item)).Group!.Single();

            var result = await reader.ReadAsync(selected, CancellationToken.None);

            result.Binding.Diagnostics.Should().BeEmpty();
            result.Binding.InternalExpansionOnly.Should().Be(_helperIdentities.Contains(entry.Identity));
            if (!result.Binding.InternalExpansionOnly)
            {
                result.Binding.Append.Should().NotBeNull();
                result.Binding.Append!.Package.Should().Be(entry.Package);
                result.Binding.Append.ConfigurationSha256.Should().Be(selected.ConfigurationSha256!.ToLowerInvariant());
                result.Binding.Append.CreateTargetFileName.Should().Be("function_app.py");
                result.Compatibility.Should().NotBeNull();
            }
        }
    }

    [Theory]
    [InlineData("packageVersion")]
    [InlineData("duplicateProperty")]
    [InlineData("unknownProperty")]
    [InlineData("unknownIdentity")]
    [InlineData("unknownSource")]
    [InlineData("createBody")]
    [InlineData("helperBody")]
    [InlineData("promptBytes")]
    [InlineData("selectedConfiguration")]
    [InlineData("helperRole")]
    [InlineData("missingMap")]
    [InlineData("malformedMap")]
    [InlineData("schemaType")]
    [InlineData("invalidUnicode")]
    public async Task PythonProvider_RealArchiveMetadataMismatch_RefusesCompatibilityAndEffectsBeforeWrites(string mismatch)
    {
        CorruptPythonProviderArchive(mismatch);
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var provider = CreatePythonProvider();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));

        var compatibility = await provider.Compatibility.ResolveAsync(selected, parsed, "Greeting", false, CancellationToken.None);
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);

        compatibility.Succeeded.Should().BeFalse();
        compatibility.Diagnostics.Should().NotBeEmpty();
        binding.Diagnostics.Should().NotBeEmpty();
        binding.Append.Should().BeNull();
        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
    }

    [Fact]
    public async Task PythonProvider_PreCanceledOriginalRead_DoesNotParseOrGenerate()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var provider = CreatePythonProvider();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> resolve = () => provider.Effects.ResolveAsync(selected, cancellation.Token);

        await resolve.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
    }

    [Fact]
    public async Task PythonProvider_OriginalHelperIsNeverAnOrdinaryCompatibilityContract()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var helper = (await templater.ResolveGroupAsync("Azure.Functions.Python.HttpTrigger.Append", TemplateType.Item)).Group!.Single();
        var provider = CreatePythonProvider();
        var parsed = new TemplateArgumentParser().Parse(new(helper.Entry.Identity, helper.Parameters, new HashSet<string>(), []));

        var result = await provider.Compatibility.ResolveAsync(helper, parsed, "Greeting", false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("Internal Python", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PythonProvider_AppendCapabilityRechecksOwnedSourceAndTarget_AndRefusesUnsupportedEffects()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var source = await new PythonTemplateSourceReader().ReadAsync(selected, CancellationToken.None);
        var root = WorkingDirectory.FromExplicit(Path.Combine(_root, "capability"));
        Directory.CreateDirectory(root.Info.FullName);
        var app = Path.Combine(root.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user code\n");
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(app)));
        var executor = Substitute.For<IPythonTemplateAppendExecutor>();
        var capability = new PythonAppendCapability(new PhysicalFileSystem(), new PythonTemplateSourceReader(), executor, selected, root);
        var append = new TemplateAppendFileEffect(source.HelperSource, app, "native expanded snippet", digest);
        var unsupported = new TemplateRestoreEffect(source.HelperSource, Path.Combine(root.Info.FullName, "project.csproj"), false);

        var validated = await capability.ValidateAsync(new(root, [append]), CancellationToken.None);
        var rejected = await capability.ExecuteAsync(unsupported, CancellationToken.None);
        File.WriteAllText(app, "new user edits");
        var changed = await capability.ExecuteAsync(append, CancellationToken.None);

        validated.Should().BeEmpty();
        rejected.Succeeded.Should().BeFalse();
        changed.Succeeded.Should().BeFalse();
        executor.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(app).Should().Be("new user edits");
    }

    [Fact]
    public async Task PythonProvider_LinkedTargetGuardRefusal_DoesNotExecuteAppend()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var source = await new PythonTemplateSourceReader().ReadAsync(selected, CancellationToken.None);
        var root = WorkingDirectory.FromExplicit(Path.Combine(_root, "capability"));
        Directory.CreateDirectory(root.Info.FullName);
        var app = Path.Combine(root.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user code");
        var executor = Substitute.For<IPythonTemplateAppendExecutor>();
        var guard = Substitute.For<PythonAppendTargetGuard>();
        guard.GetDiagnostic(root.Info.FullName, app).Returns("Linked target refused.");
        var capability = new PythonAppendCapability(new PhysicalFileSystem(), new PythonTemplateSourceReader(), executor, selected, root, guard);
        var append = new TemplateAppendFileEffect(source.HelperSource, app, "expanded", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(app))));

        var result = await capability.ExecuteAsync(append, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain("Linked target refused.");
        executor.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(app).Should().Be("user code");
    }

    [Fact]
    public void PythonProvider_NullDependencies_AreRejected()
    {
        var sources = new PythonTemplateSourceReader();
        var interaction = Substitute.For<IInteractionService>();
        var files = new PhysicalFileSystem();
        var executor = new PythonTemplateAppendExecutor();
        Action[] constructors =
        [
            () => new PythonItemCompatibility(null!, sources),
            () => new PythonItemCompatibility(interaction, null!),
            () => new PythonSelectedItemEffects(null!, sources, executor, false),
            () => new PythonSelectedItemEffects(files, null!, executor, false),
            () => new PythonSelectedItemEffects(files, sources, null!, false),
            () => new PythonItemWorkflowProvider(null!, new(files, sources, executor, false)),
            () => new PythonItemWorkflowProvider(new(interaction, sources), null!),
        ];
        foreach (var constructor in constructors)
        {
            constructor.Should().Throw<ArgumentNullException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PythonProvider_TargetChangedOrCanceledAfterActualPrivateExpansion_DoesNotAppend(bool cancel)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var root = WorkingDirectory.FromExplicit(Path.Combine(_root, "production-changed"));
        Directory.CreateDirectory(root.Info.FullName);
        var app = Path.Combine(root.Info.FullName, "function_app.py");
        File.WriteAllText(app, "original user bytes");
        PhysicalFileSystem physical = new();
        var files = Substitute.For<IFileSystem>();
        using CancellationTokenSource cancellation = new();
        files.FileExists(Arg.Any<string>()).Returns(call => physical.FileExists(call.Arg<string>()));
        files.DirectoryExists(Arg.Any<string>()).Returns(call => physical.DirectoryExists(call.Arg<string>()));
        files.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.ComputeSha256Async(call.Arg<string>(), call.Arg<CancellationToken>()));
        files.CreateTempDirectory().Returns(_ => physical.CreateTempDirectory());
        files.GetFileSystemEntries(Arg.Any<string>()).Returns(call => physical.GetFileSystemEntries(call.Arg<string>()));
        bool observedExpansion = false;
        files.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var path = call.Arg<string>();
            var content = await physical.ReadAllTextIfExistsAsync(path, call.Arg<CancellationToken>());
            if (Path.GetFileName(path) == "function_body.py")
            {
                content.Should().Contain("def Greeting(");
                observedExpansion = true;
                if (cancel)
                {
                    cancellation.Cancel();
                }
                else
                {
                    File.WriteAllText(app, "new user bytes");
                }
            }
            return content;
        });
        PythonSelectedItemEffects effects = new(files, new PythonTemplateSourceReader(), new PythonTemplateAppendExecutor(), confirmed: true);
        var binding = await effects.ResolveAsync(selected, cancellation.Token);
        var request = new TemplateInvocationRequest(root, new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "ANONYMOUS" }, "Greeting");

        if (cancel)
        {
            Func<Task> invoke = () => effects.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            await invoke.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await effects.InvokeAsync(templater, selected, binding, request, cancellation.Token);
            result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
            result.Effects!.Kind.Should().Be("preflightFailed");
        }

        observedExpansion.Should().BeTrue("the actual native helper must run before the instrumented I/O boundary changes state");
        File.ReadAllText(app).Should().Be(cancel ? "original user bytes" : "new user bytes");
        Directory.GetFiles(root.Info.FullName).Should().ContainSingle();
    }

    [Fact]
    public void PythonProvider_HostHelpersDoNotReferencePhysicalStackAssemblyOrIntroducePublicTypes()
    {
        var host = typeof(PythonItemWorkflowProvider).Assembly;
        host.GetReferencedAssemblies().Select(assembly => assembly.Name).Should().NotContain("Azure.Functions.Cli.Workloads.Python");
        typeof(PythonItemWorkflowProvider).IsPublic.Should().BeFalse();
        typeof(PythonProjectWorkflowProvider).IsPublic.Should().BeFalse();
        typeof(PythonTemplateAppendExecutor).Assembly.Should().BeSameAs(host);
        typeof(PythonTemplateAppendExecutor).IsPublic.Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task PythonProvider_RealTargetLinkRefuses_WhileExplicitLinkedRootRetainsItsBoundary()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var source = await new PythonTemplateSourceReader().ReadAsync(selected, CancellationToken.None);
        var targetRoot = Directory.CreateDirectory(Path.Combine(_root, "real-target"));
        var realApp = Path.Combine(targetRoot.FullName, "function_app.py");
        File.WriteAllText(realApp, "user code\n");
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(realApp)));
        var outside = Directory.CreateDirectory(Path.Combine(_root, "target-link"));
        var targetLink = Path.Combine(outside.FullName, "function_app.py");
        File.CreateSymbolicLink(targetLink, realApp);
        var deniedRoot = WorkingDirectory.FromExplicit(outside.FullName);
        var capability = new PythonAppendCapability(new PhysicalFileSystem(), new PythonTemplateSourceReader(),
            new PythonTemplateAppendExecutor(), selected, deniedRoot);

        var refused = await capability.ExecuteAsync(new TemplateAppendFileEffect(source.HelperSource, targetLink, "must not append", digest),
            CancellationToken.None);

        refused.Succeeded.Should().BeFalse();
        refused.Diagnostics.Should().Contain(item => item.Contains("linked", StringComparison.Ordinal));
        File.ReadAllText(realApp).Should().Be("user code\n");
        File.Delete(targetLink);

        var rootLink = Path.Combine(_root, "explicit-root-link");
        Directory.CreateSymbolicLink(rootLink, targetRoot.FullName);
        var allowedRoot = WorkingDirectory.FromExplicit(rootLink);
        var linkedApp = Path.Combine(rootLink, "function_app.py");
        var allowed = new PythonAppendCapability(new PhysicalFileSystem(), new PythonTemplateSourceReader(),
            new PythonTemplateAppendExecutor(), selected, allowedRoot);
        var result = await allowed.ExecuteAsync(new TemplateAppendFileEffect(source.HelperSource, linkedApp, "approved append", digest),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Diagnostics));
        File.ReadAllText(realApp).Should().Be("user code\napproved append");
        Directory.Delete(rootLink);
    }

    private static PythonItemWorkflowProvider CreatePythonProvider(bool confirmed = true)
    {
        var sources = new PythonTemplateSourceReader();
        return new(new(Substitute.For<IInteractionService>(), sources),
            new(new PhysicalFileSystem(), sources, new PythonTemplateAppendExecutor(), confirmed));
    }

    private void CorruptPythonProviderArchive(string corruption)
    {
        using var archive = ZipFile.Open(_archive, ZipArchiveMode.Update);
        var manifestPath = "source/applications.json";
        var text = ReadEntry(manifestPath);
        var map = JsonNode.Parse(text)!;
        var application = map["applications"]!.AsArray().Single(item => item!["templateIdentity"]!.GetValue<string>() == "Azure.Functions.Python.HttpTrigger")!;
        switch (corruption)
        {
            case "packageVersion": map["version"] = "99.0.0"; break;
            case "unknownProperty": map["notSupported"] = true; break;
            case "unknownIdentity": application["templateIdentity"] = "Unknown.Native.Python"; break;
            case "unknownSource": map["sources"]![0]!["path"] = "source/other.json"; break;
            case "createBody": WriteEntry(application["createSource"]!.GetValue<string>(), "changed user source"); return;
            case "helperBody": WriteEntry(application["appendHelper"]!["source"]!.GetValue<string>(), "changed helper source"); return;
            case "promptBytes": WriteEntry("source/userPrompts.json", ReadEntry("source/userPrompts.json") + "\n"); return;
            case "selectedConfiguration": WriteEntry(application["configuration"]!.GetValue<string>(), ReadEntry(application["configuration"]!.GetValue<string>()) + "\n"); return;
            case "helperRole":
                var path = application["appendHelper"]!["configuration"]!.GetValue<string>();
                WriteEntry(path, ReadEntry(path).Replace("python-append-helper", "different-role", StringComparison.Ordinal));
                return;
            case "missingMap": archive.GetEntry(manifestPath)!.Delete(); return;
            case "malformedMap": WriteEntry(manifestPath, "{not-json"); return;
            case "schemaType": map["schemaVersion"] = "one"; break;
            case "invalidUnicode":
                WriteEntry(manifestPath, map.ToJsonString().Replace($"\"version\":\"{map["version"]!.GetValue<string>()}\"",
                    "\"version\":\"\\uD800\"", StringComparison.Ordinal));
                return;
            case "duplicateProperty": WriteEntry(manifestPath, text.Insert(text.LastIndexOf('}'), ",\"version\":\"duplicated\"")); return;
            default: throw new InvalidOperationException("Unknown archive corruption fixture.");
        }

        WriteEntry(manifestPath, map.ToJsonString());
        return;

        string ReadEntry(string path)
        {
            using StreamReader reader = new(archive.GetEntry(path)!.Open());
            return reader.ReadToEnd();
        }

        void WriteEntry(string path, string content)
        {
            archive.GetEntry(path)?.Delete();
            using StreamWriter writer = new(archive.CreateEntry(path).Open());
            writer.Write(content);
        }
    }
}
