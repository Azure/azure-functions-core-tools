// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using Azure.Functions.Cli.Commands;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeSelectedItemAppendSupportTests : IDisposable
{
    private const string UserIdentity = "Controlled.User";
    private const string HelperIdentity = "Controlled.Internal";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-append-expansion-" + Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileSystem _fileSystem = new();

    private string Package => Path.Combine(_root, "package");
    private string Output => Path.Combine(_root, "output");
    private string Target => Path.Combine(Output, "function_app.py");
    private TemplateEngineContext Context => new(WorkingDirectory.FromExplicit(Output),
        new(WorkingDirectory.FromExplicit(Output), "python", "Python"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_ActualNativeHelperPreviewThenAppend_UsesCanonicalArgumentsNameAndSameContextWithoutOverwriting(bool archive)
    {
        using var templater = await OpenAsync(archive: archive);
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        var mapping = Mapping(selected, helper);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "# user-owned\n");
        var capability = new AppendCapability(_fileSystem);
        var support = Support(mapping, capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Output),
            new Dictionary<string, string?> { ["Value"] = "CanonicalValue" }, "ChosenName", DryRun: true);

        var preview = await support.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        preview!.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Effects!.Kind.Should().Be("previewed");
        var effect = preview.Effects.Plan.Effects.Should().ContainSingle().Subject.Should().BeOfType<TemplateAppendFileEffect>().Subject;
        effect.Content.Should().Be("\nChosenName|CanonicalValue|python");
        File.ReadAllText(Target).Should().Be("# user-owned\n");
        Directory.GetFileSystemEntries(Output).Should().Equal(Target);
        capability.Executions.Should().Be(0);

        var created = await support.InvokeAsync(templater, selected, binding, request with { DryRun = false }, CancellationToken.None);

        created!.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllText(Target).Should().Be("# user-owned\n\nChosenName|CanonicalValue|python");
        capability.Executions.Should().Be(1);
        created.Changes.Should().BeEmpty();
        created.HasCommittedOutput.Should().BeFalse();
        created.Effects!.CompletedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplateAppendFileEffect>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_ExplicitInternalHelper_IsRejectedButOriginalHiddenUserItemStillWorks(bool includeCapability)
    {
        using var templater = await OpenAsync(hiddenUser: true);
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        var support = Support(Mapping(selected, helper), new AppendCapability(_fileSystem));
        var contexts = Substitute.For<INewExecutionContextResolver>();
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), false, Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(Context, null));
        var factory = Substitute.For<ITemplaterFactory>();
        factory.CreateAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>()).Returns(templater);
        TestInteractionService interaction = new();
        TemplateArgumentParser parser = new();
        NewExecutionRunner runner = new(contexts, factory, parser, interaction, new(parser, interaction));
        var request = new NewExecutionRequest(WorkingDirectory.FromExplicit(Output), HelperIdentity, "Named", null,
            false, true, false, false, false, [], new HashSet<string>()) { SelectedItemEffects = includeCapability ? support : null };

        var result = await runner.ExecuteAsync(request, new([]), CancellationToken.None);

        result.Kind.Should().Be("internalTemplate");
        result.Succeeded.Should().BeFalse();
        Directory.Exists(Output).Should().BeFalse();

        using var second = await OpenExistingAsync();
        var original = (await second.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        original.Entry.IsHidden.Should().BeTrue();
        var originalBinding = await support.ResolveAsync(original, CancellationToken.None);
        originalBinding.InternalExpansionOnly.Should().BeFalse();
        var native = await original.InvokeAsync(new(WorkingDirectory.FromExplicit(Output),
            new Dictionary<string, string?> { ["Value"] = "Original" }, "Named"), CancellationToken.None);
        native.Status.Should().Be(TemplateInvocationStatus.Success, native.Diagnostic);
        File.ReadAllText(Target).Should().Be("full:Named|Original");
    }

    [Theory]
    [InlineData("success")]
    [InlineData("noop")]
    [InlineData("failure")]
    [InlineData("throw")]
    public async Task Adapter_ActualAppendThenCancellation_PreservesReturnedOutcomeOrOriginalUnknownStateException(string outcome)
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "user-owned");
        using CancellationTokenSource cancellation = new();
        var original = new OperationCanceledException("append state unknown", cancellation.Token);
        var capability = new AppendCapability(_fileSystem)
        {
            SkipWrite = outcome == "noop",
            AfterExecution = () =>
            {
                cancellation.Cancel();
                if (outcome == "throw") throw original;
            },
            Outcome = new(outcome != "failure", ["actual returned append diagnostic"], TargetMayHaveChanged: outcome == "failure"),
        };
        var support = Support(Mapping(selected, helper), capability);
        var contexts = Substitute.For<INewExecutionContextResolver>();
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), false, Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(Context, null));
        var factory = Substitute.For<ITemplaterFactory>();
        factory.CreateAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>()).Returns(templater);
        TestInteractionService interaction = new();
        TemplateArgumentParser parser = new();
        NewExecutionRunner runner = new(contexts, factory, parser, interaction, new(parser, interaction));
        NativeTemplateCommandAdapter adapter = new(Substitute.For<IInitExecutionRunner>(), runner, new(interaction), new(interaction), new([]),
            selectedItemEffects: support);
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        var parsed = adapter.ParseNewInvocation(command, [Output, "-t", UserIdentity, "-n", "Named", "--non-interactive", "--output", "json"]);

        if (outcome == "throw")
        {
            Func<Task> act = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);
            var thrown = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
            thrown.Should().BeSameAs(original);
            thrown.CancellationToken.Should().Be(cancellation.Token);
            interaction.AllOutput.Should().Contain("0 completed").And.Contain("outcome is unknown").And.NotContain("JSON:");
        }
        else
        {
            int exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);
            exit.Should().Be(outcome == "failure" ? 1 : 0);
            interaction.AllOutput.Should().Contain("JSON:").And.Contain("actual returned append diagnostic")
                .And.Contain("\"changes\":[]");
        }
        capability.Executions.Should().Be(1);
        File.ReadAllText(Target).Should().Be(outcome == "noop" ? "user-owned" : "user-owned\nNamed|default|python");
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("source")]
    [InlineData("package")]
    [InlineData("configuration")]
    public async Task Invoke_SourceOrPackageMismatch_RejectsBeforeLiveAppend(string mismatch)
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        var mapping = Mapping(selected, helper);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "user-owned");
        if (mismatch == "package") mapping = mapping with { Package = new("other-package", "2.0.0") };
        if (mismatch == "configuration") mapping = mapping with { HelperConfigurationSha256 = new string('a', 64) };
        if (mismatch == "mapping") File.AppendAllText(Path.Combine(Package, "source", "applications.json"), "changed");
        if (mismatch == "source") File.AppendAllText(Path.Combine(Package, "helper", "function_body.py"), "changed");
        var capability = new AppendCapability(_fileSystem);
        var support = Support(mapping, capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);

        var result = await support.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(Output), new Dictionary<string, string?>(), "Named"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        File.ReadAllText(Target).Should().Be("user-owned");
        capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_UnexpectedNativeOutputOrParameters_RejectsBeforeLiveAppend(bool invalidParameters)
    {
        using var templater = await OpenAsync(extraHelperOutput: !invalidParameters);
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "user-owned");
        var capability = new AppendCapability(_fileSystem);
        var support = Support(Mapping(selected, helper), capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);
        Dictionary<string, string?> values = invalidParameters ? new() { ["Unknown"] = "invalid" } : [];

        var result = await support.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(Output), values, "Named"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        File.ReadAllText(Target).Should().Be("user-owned");
        capability.Executions.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_TargetChangedDuringExpansion_RejectsCapturedHashWithoutOverwritingNewUserContent()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "before");
        var capability = new AppendCapability(_fileSystem);
        var support = Support(Mapping(selected, helper), capability, onHelper: () => File.WriteAllText(Target, "new user content"));
        var binding = await support.ResolveAsync(selected, CancellationToken.None);

        var result = await support.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(Output), new Dictionary<string, string?>(), "Named"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Effects!.Kind.Should().Be("preflightFailed");
        File.ReadAllText(Target).Should().Be("new user content");
        capability.Executions.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_UnconfirmedNativeExpansion_DoesNotAppend()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "user-owned");
        var capability = new AppendCapability(_fileSystem);
        var support = Support(Mapping(selected, helper), capability, confirmed: false);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);

        var result = await support.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(Output), new Dictionary<string, string?>(), "Named"), CancellationToken.None);

        result!.Effects!.Kind.Should().Be("declined");
        File.ReadAllText(Target).Should().Be("user-owned");
        capability.Executions.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_CancellationAfterPrivateExpansion_DoesNotAppend()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "user-owned");
        using CancellationTokenSource cancellation = new();
        var capability = new AppendCapability(_fileSystem) { BeforeValidation = cancellation.Cancel };
        var support = Support(Mapping(selected, helper), capability);
        var binding = await support.ResolveAsync(selected, cancellation.Token);

        Func<Task> act = () => support.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(Output), new Dictionary<string, string?>(), "Named"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(Target).Should().Be("user-owned");
        capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_OriginalConfigurationChanged_UsesExistingFingerprintRefusal(bool duringExpansion)
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        var mapping = Mapping(selected, helper);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "user-owned");
        Action change = () => File.AppendAllText(Path.Combine(Package, "create", ".template.config", "template.json"), Environment.NewLine);
        if (!duringExpansion) change();
        var capability = new AppendCapability(_fileSystem);
        var support = Support(mapping, capability, onHelper: duringExpansion ? change : null);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);

        var result = await support.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(Output), new Dictionary<string, string?>(), "Named"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        File.ReadAllText(Target).Should().Be("user-owned");
        capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_MissingEffectiveName_RefusesInsteadOfUsingRandomPrivateDirectory(bool dryRun)
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];
        var helper = (await templater.ResolveGroupAsync(HelperIdentity, TemplateType.Item)).Group![0];
        Directory.CreateDirectory(Output);
        File.WriteAllText(Target, "user-owned");
        var capability = new AppendCapability(_fileSystem);
        var support = Support(Mapping(selected, helper), capability);
        var binding = await support.ResolveAsync(selected, CancellationToken.None);

        var result = await support.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(Output), new Dictionary<string, string?>(), DryRun: dryRun), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Diagnostic.Should().Contain("effective name");
        File.ReadAllText(Target).Should().Be("user-owned");
        capability.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedPackageReader_RejectsTraversalAndRemainsBoundToInstalledSource(bool archive)
    {
        using var templater = await OpenAsync(archive: archive);
        var selected = (await templater.ResolveGroupAsync(UserIdentity, TemplateType.Item)).Group![0];

        var bytes = await selected.ReadPackageFileAsync("create/function_app.py", CancellationToken.None);
        Func<Task> traversal = () => selected.ReadPackageFileAsync("../outside.py", CancellationToken.None);

        Encoding.UTF8.GetString(bytes.Span).Should().Be("full:TemplateName|TOKEN");
        await traversal.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("[]")]
    [InlineData("\"\"")]
    [InlineData("\" internal \"")]
    public void InternalRole_MalformedDeclaration_NeverFallsBackToOrdinaryItem(string role)
    {
        Action act = () => TemplateCatalogProjector.ReadInternalRole("{\"tags\":{\"type\":\"item\",\"func-role\":" + role + "}}");

        act.Should().Throw<InvalidTemplateMetadataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private NativeSelectedItemAppendSupport Support(NativeItemAppendMapping mapping, INativeTemplateEffectCapability capability,
        bool confirmed = true, Action? onHelper = null) => new(_fileSystem, capability, (selected, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool helper = selected.Entry.Identity == HelperIdentity;
        if (helper) onHelper?.Invoke();
        return Task.FromResult(new NativeSelectedItemBinding(helper, helper ? null : mapping, []));
    }, confirmed);

    private NativeItemAppendMapping Mapping(ResolvedTemplate selected, ResolvedTemplate helper) => new(
        selected.Entry.Package!, selected.ConfigurationSha256!, Source(UserIdentity, "source/applications.json"),
        Source(UserIdentity, "create/function_app.py"), "function_app.py", "function_app.py", HelperIdentity,
        helper.ConfigurationSha256!, Source(HelperIdentity, "helper/function_body.py"), "function_body.py");

    private TemplateEffectSource Source(string identity, string path) =>
        new(identity, path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Package, path.Replace('/', Path.DirectorySeparatorChar))))));

    private async Task<Templater> OpenAsync(bool hiddenUser = false, bool extraHelperOutput = false, bool archive = false)
    {
        WriteTemplate("create", UserIdentity, "function_app.py", "full:TemplateName|TOKEN", hiddenUser);
        WriteTemplate("helper", HelperIdentity, "function_body.py", "\nTemplateName|TOKEN|STACK", hidden: true);
        if (extraHelperOutput) File.WriteAllText(Path.Combine(Package, "helper", "unexpected.txt"), "unexpected");
        Directory.CreateDirectory(Path.Combine(Package, "source"));
        File.WriteAllText(Path.Combine(Package, "source", "applications.json"), """{"ownedMapping":true}""");
        string install = Package;
        if (archive)
        {
            File.WriteAllText(Path.Combine(Package, "Controlled.Package.nuspec"), """
                <package><metadata><id>Controlled.Package</id><version>1.0.0</version><authors>Tests</authors>
                <description>Real mounted archive fixture</description><packageTypes><packageType name="Template" /></packageTypes>
                </metadata></package>
                """);
            install = Path.Combine(_root, "Controlled.Package.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(Package, install);
        }

        await TemplateFixtures.InstallAsync(install, Path.Combine(_root, "hive"), CancellationToken.None);
        return await OpenExistingAsync();
    }

    private Task<Templater> OpenExistingAsync() => Task.FromResult(TemplateFixtures.CreateTemplater(new TemplateEngineSession(Context, Path.Combine(_root, "hive"))));

    private void WriteTemplate(string folder, string identity, string output, string content, bool hidden)
    {
        string directory = Path.Combine(Package, folder);
        Directory.CreateDirectory(Path.Combine(directory, ".template.config"));
        string role = identity == HelperIdentity ? "\"func-role\":\"controlled-expansion\"," : string.Empty;
        File.WriteAllText(Path.Combine(directory, ".template.config", "template.json"), $$$$"""
            {"identity":"{{{{identity}}}}","name":"{{{{identity}}}}","shortName":"{{{{identity}}}}",
             "tags":{ {{{{role}}}}"type":"item","language":"Python"},"sourceName":"TemplateName",
             "symbols":{"Value":{"type":"parameter","datatype":"string","defaultValue":"default","replaces":"TOKEN"},
               "Stack":{"type":"bind","binding":"host:func:stack","replaces":"STACK"}}}
            """);
        File.WriteAllText(Path.Combine(directory, ".template.config", "func.host.json"), hidden ? """{"isHidden":true}""" : "{}");
        File.WriteAllText(Path.Combine(directory, output), content);
    }

    private sealed class AppendCapability(PhysicalFileSystem fileSystem) : INativeTemplateEffectCapability
    {
        public int Executions { get; private set; }

        public Action? BeforeValidation { get; init; }

        public Action? AfterExecution { get; init; }

        public bool SkipWrite { get; init; }

        public TemplateEffectExecutionResult? Outcome { get; init; }

        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            BeforeValidation?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var effect in plan.Effects)
            {
                if (effect is not TemplateAppendFileEffect append
                    || await fileSystem.ComputeSha256Async(append.TargetPath, cancellationToken) != append.ExpectedTargetSha256)
                {
                    return ["Target snapshot changed."];
                }
            }

            return [];
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            var append = (TemplateAppendFileEffect)effect;
            if (await fileSystem.ComputeSha256Async(append.TargetPath, cancellationToken) != append.ExpectedTargetSha256)
            {
                return new(false, ["Target snapshot changed."]);
            }

            if (!SkipWrite)
            {
                await File.AppendAllTextAsync(append.TargetPath, append.Content, cancellationToken);
            }
            Executions++;
            AfterExecution?.Invoke();
            return Outcome ?? new(true, []);
        }
    }
}
