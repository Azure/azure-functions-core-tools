// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class NativeMsBuildSessionBindingTests : IDisposable
{
    private const string Identity = "Controlled.MsBuild.Item";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-msbuild-session-" + Guid.NewGuid().ToString("N"));

    private string Output => Path.Combine(_root, "project");
    private string Project => Path.Combine(Output, "Selected.csproj");
    private WorkingDirectory Directory => WorkingDirectory.FromExplicit(Output);

    [Fact]
    public async Task Invoke_ExplicitSourceBoundSnapshot_ControlsActualNativeBindAndEvaluatedDependencyConditions()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(Identity, TemplateType.Item)).Group![0];
        var capability = Capability();
        var request = Request(selected, capability);

        var preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Effects!.Plan.Effects.Should().ContainSingle().Which.Should().BeOfType<TemplatePackageReferenceEffect>()
            .Which.Version.Should().Be("2.0.0");
        File.Exists(Path.Combine(Output, "bound.txt")).Should().BeFalse();
        await capability.DidNotReceive().ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>());

        var created = await selected.InvokeAsync(request, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllText(Path.Combine(Output, "bound.txt")).Should().Be("net10.0|Imported.Namespace");
        await capability.Received(1).ExecuteAsync(Arg.Is<TemplatePackageReferenceEffect>(effect => effect.Version == "2.0.0"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("template")]
    [InlineData("source")]
    [InlineData("fingerprint")]
    [InlineData("project")]
    public async Task Invoke_MismatchedSnapshot_RefusesBeforeNativePreviewOrEffects(string mismatch)
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(Identity, TemplateType.Item)).Group![0];
        var capability = Capability();
        var request = Request(selected, capability);
        NativeMsBuildBindingSnapshot snapshot = new(
            mismatch == "project" ? Path.Combine(Output, "Other.csproj") : Project,
            mismatch == "template" ? "Other.Template" : Identity,
            mismatch == "source" ? "other-source" : selected.ConfigurationSourceIdentity!,
            mismatch == "fingerprint" ? new string('a', 64) : selected.ConfigurationSha256!,
            "net10.0", "Imported.Namespace");

        var result = await selected.InvokeAsync(request with { MsBuildBindings = snapshot }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        result.Diagnostic.Should().Contain("does not match");
        File.Exists(Path.Combine(Output, "bound.txt")).Should().BeFalse();
        capability.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Invoke_ControlledEffectsWithoutSnapshot_RefusesNativeNet8Fallback()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(Identity, TemplateType.Item)).Group![0];
        var capability = Capability();

        var result = await selected.InvokeAsync(Request(selected, capability) with { MsBuildBindings = null }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        result.Diagnostic.Should().Contain("framework defaults are not a project binding");
        File.Exists(Path.Combine(Output, "bound.txt")).Should().BeFalse();
        capability.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Invoke_ControlledSessionCannotLeakOrReplaceSnapshot()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(Identity, TemplateType.Item)).Group![0];
        var request = Request(selected, Capability()) with { DryRun = true };
        var preview = await selected.InvokeAsync(request, CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);

        var absent = await selected.InvokeAsync(request with { MsBuildBindings = null }, CancellationToken.None);
        var changed = await selected.InvokeAsync(request with
        {
            MsBuildBindings = new(Project, Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!,
                "net10.0", "Different.Namespace"),
        }, CancellationToken.None);

        absent.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        changed.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        File.Exists(Path.Combine(Output, "bound.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_NoCapabilities_DefaultPostActionDenialRemains()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(Identity, TemplateType.Item)).Group![0];

        var result = await selected.InvokeAsync(new(Directory, new Dictionary<string, string?>(), "Named"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("qualified dispatcher");
        File.Exists(Path.Combine(Output, "bound.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Invoke_FSharpRepair_UsesActualCapturedBytesBeforeInstallAndRechecksExistingProject(bool wrongGeneratedHash, bool changedProject)
    {
        string package = Path.Combine(_root, "fsharp");
        System.IO.Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        System.IO.Directory.CreateDirectory(Output);
        string project = Path.Combine(Output, "Selected.fsproj");
        string program = Path.Combine(Output, "Program.fs");
        string generated = Path.Combine(Output, "Handler.fs");
        File.WriteAllText(project, "<Project />");
        File.WriteAllText(program, "module Program");
        const string body = "module Handler";
        File.WriteAllText(Path.Combine(package, "Handler.fs"), body);
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), """
            {"identity":"Controlled.FSharp","name":"FSharp","shortName":"controlled-fsharp",
             "tags":{"type":"item","language":"F#"},"primaryOutputs":[{"path":"Handler.fs"}]}
            """);
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive);
        using Templater templater = TemplateFixtures.CreateTemplater(new(new(Directory, new(Directory, "dotnet", "F#")), hive));
        var selected = (await templater.ResolveGroupAsync("Controlled.FSharp", TemplateType.Item)).Group![0];
        var capability = Capability();
        TemplateEffectSource source = new(selected.Entry.Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!);
        string projectHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project)));
        TemplateFSharpCompileEffect repair = new(source, project, projectHash, [program],
            [new(generated, wrongGeneratedHash ? new('A', 64) : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))))]);
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var plan = call.Arg<NativeTemplateEffectPlan>();
            plan.NativeCapture.Should().NotBeNull();
            Encoding.UTF8.GetString(plan.NativeCapture!.ExpandedFiles["Handler.fs"].Span).Should().Be(body);
            if (changedProject) File.AppendAllText(project, "changed");
            IReadOnlyList<string> diagnostics = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(project))) == repair.ExpectedProjectSha256
                ? [] : ["Project changed."];
            return diagnostics;
        });
        capability.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<TemplateEffect>().Should().Be(repair);
            File.ReadAllText(generated).Should().Be(body);
            return new TemplateEffectExecutionResult(true, []);
        });

        var result = await selected.InvokeAsync(new(Directory, new Dictionary<string, string?>(), "Handler")
        {
            MsBuildBindings = new(project, selected.Entry.Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!, "net10.0", "Functions"),
            EffectSupport = new(Directory, project, capability, Confirmed: true) { FSharpRepair = repair },
        }, CancellationToken.None);

        result.Status.Should().Be(wrongGeneratedHash ? TemplateInvocationStatus.InvalidTemplate
            : changedProject ? TemplateInvocationStatus.EffectFailure : TemplateInvocationStatus.Success, result.Diagnostic);
        File.Exists(generated).Should().Be(!wrongGeneratedHash && !changedProject);
        if (wrongGeneratedHash)
        {
            capability.ReceivedCalls().Should().BeEmpty();
        }
        else if (!changedProject)
        {
            result.Effects!.CompletedEffects.Should().Equal(repair);
        }
        File.ReadAllText(project).Should().Be(changedProject ? "<Project />changed" : "<Project />");
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_root))
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
    }

    private TemplateInvocationRequest Request(ResolvedTemplate selected, INativeTemplateEffectCapability capability) =>
        new(Directory, new Dictionary<string, string?>(), "Named")
        {
            EffectSupport = new(Directory, Project, capability, Confirmed: true),
            MsBuildBindings = new(Project, Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!,
                "net10.0", "Imported.Namespace"),
        };

    private static INativeTemplateEffectCapability Capability()
    {
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns([]);
        capability.ExecuteAsync(Arg.Any<TemplateEffect>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateEffectExecutionResult(true, []));
        return capability;
    }

    private async Task<Templater> OpenAsync()
    {
        string package = Path.Combine(_root, "package");
        System.IO.Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        System.IO.Directory.CreateDirectory(Output);
        File.WriteAllText(Project, """<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        File.WriteAllText(Path.Combine(package, "bound.txt"), "FRAMEWORK|NAMESPACE");
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), $$$$"""
            {"identity":"Controlled.MsBuild.Item","name":"Controlled MSBuild","shortName":"controlled-msbuild",
             "tags":{"type":"item","language":"C#"},
             "symbols":{"Framework":{"type":"bind","binding":"msbuild:TargetFramework","defaultValue":"net8.0","replaces":"FRAMEWORK"},
               "Namespace":{"type":"bind","binding":"msbuild:RootNamespace","defaultValue":"Default.Namespace","replaces":"NAMESPACE"}},
             "postActions":[
               {"condition":"(Framework == \"net10.0\")","actionId":"{{{{NativeTemplateEffectPlanner.PackageReferenceActionId}}}}",
                "args":{"referenceType":"package","reference":"Controlled.Package","version":"2.0.0","projectFileExtensions":".csproj"}},
               {"condition":"(Framework != \"net10.0\")","actionId":"{{{{NativeTemplateEffectPlanner.PackageReferenceActionId}}}}",
                "args":{"referenceType":"package","reference":"Controlled.Package","version":"1.0.0","projectFileExtensions":".csproj"}}]}
            """);
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive, CancellationToken.None);
        return TemplateFixtures.CreateTemplater(new(new(Directory, new(Directory, "dotnet", "C#")), hive));
    }
}
