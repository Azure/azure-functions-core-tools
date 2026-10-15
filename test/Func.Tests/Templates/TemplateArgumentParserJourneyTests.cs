// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class TemplateArgumentParserJourneyTests : IDisposable
{
    private const string LowIdentity = "Func.Tests.Parser.Low";
    private const string HighIdentity = "Func.Tests.Parser.High";
    private const string ShortName = "func-tests-parser";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-parser-journey-" + Guid.NewGuid().ToString("N"));
    private readonly ITemplateArgumentParser _parser = new TemplateArgumentParser();
    private readonly IReadOnlySet<string> _reserved = new HashSet<string> { "--name", "-n", "--output", "--force", "-f", "--help" };

    [Fact]
    public async Task Parse_RawArgumentsBeforePrecedence_SelectsInstalledItemAndPreviewsThenCreatesThatCapability()
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "functions", "nested"));
        string[] raw = ["--greeting=Hello from parser", "-p:f", "true"];
        string[] unchanged = [.. raw];
        using var templater = await CreateTemplaterAsync(output);
        var resolution = await templater.ResolveGroupAsync(ShortName, TemplateType.Item, CancellationToken.None);
        var original = resolution.Group!;
        var language = original.FilterByLanguage("javascript");
        var parsed = language.Select(template => Parse(template, raw)).ToArray();

        var selectedGroup = language.FilterByArguments(
            parsed.Where(result => result.IsArgumentCompatible).Select(result => result.TemplateIdentity).ToHashSet(StringComparer.Ordinal))
            .FilterToHighestPrecedence();
        var selected = selectedGroup.Should().ContainSingle().Subject;
        var final = parsed.Should().ContainSingle(result => result.TemplateIdentity == selected.Entry.Identity).Subject;
        var request = new TemplateInvocationRequest(output, final.ValuesByCanonicalName, "Generated", DryRun: true);
        var before = SnapshotProject();
        var preview = await selected.InvokeAsync(request, CancellationToken.None);

        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        original.Should().HaveCount(2);
        original.FilterToHighestPrecedence().Should().ContainSingle().Which.Entry.Identity.Should().Be(HighIdentity);
        selected.Entry.Identity.Should().Be(LowIdentity);
        selected.Should().BeSameAs(original.Single(template => template.Entry.Identity == LowIdentity));
        final.IsComplete.Should().BeTrue();
        final.ValuesByCanonicalName.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["Greeting"] = "Hello from parser", ["AuthLevel"] = "anonymous", ["Feature"] = "true",
        });
        selected.Parameters.Should().NotContain(parameter => parameter.CanonicalName == "name");
        final.Parameters.Should().Contain(parameter => parameter.LongAlias == "--param:AuthLevel");
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        Path.GetFullPath(Path.Combine(output.Info.FullName, preview.Changes.Should().ContainSingle().Which.Path))
            .Should().Be(Path.Combine(output.Info.FullName, "Generated.txt"));
        Path.GetRelativePath(output.Info.FullName, Path.GetFullPath(Path.Combine(output.Info.FullName, preview.Changes[0].Path)))
            .Should().Be("Generated.txt");
        SnapshotProject().Should().BeEquivalentTo(before);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        raw.Should().Equal(unchanged);

        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Generated.txt"))
            .Should().Be($"Generated|Hello from parser|anonymous|True|{output.Info.FullName}|{Path.Combine(_root, "project")}");
        File.Exists(Path.Combine(output.Info.FullName, "High.txt")).Should().BeFalse();
        original.Should().HaveCount(2);
    }

    [Fact]
    public async Task Parse_MissingVisibleRequired_RemainsSelectableAndCompletionIsAuthoritative()
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "completion"));
        using var templater = await CreateTemplaterAsync(output);
        var selected = (await templater.ResolveGroupAsync(LowIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var raw = new[] { "--param:AuthLevel", "function", "-p:f", "false" };
        var before = SnapshotProject();

        var incomplete = Parse(selected, raw);
        var invalid = Parse(selected, raw, new Dictionary<string, string?> { ["Greeting"] = "hello", ["AuthLevel"] = "invalid" });
        var complete = Parse(selected, raw, new Dictionary<string, string?> { ["Greeting"] = "Prompted" });

        incomplete.IsArgumentCompatible.Should().BeTrue();
        incomplete.IsComplete.Should().BeFalse();
        incomplete.MissingRequiredParameters.Should().ContainSingle().Which.LongAlias.Should().Be("--greeting");
        invalid.IsComplete.Should().BeFalse();
        invalid.InvalidInputDiagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains("cannot replace", StringComparison.Ordinal));
        complete.IsComplete.Should().BeTrue();
        var preview = await selected.InvokeAsync(new(output, complete.ValuesByCanonicalName, "Completed", DryRun: true), CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        SnapshotProject().Should().BeEquivalentTo(before);
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await selected.InvokeAsync(new(output, complete.ValuesByCanonicalName, "Completed"), CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Completed.txt")).Should().StartWith("Completed|Prompted|function|False|");
    }

    [Theory]
    [InlineData("--unknown", "value")]
    [InlineData("--greeting")]
    [InlineData("--greeting", "hello", "--param:AuthLevel", "invalid")]
    [InlineData("--greeting", "hello", "-p:f", "invalid")]
    public async Task Parse_InvalidRawInput_EliminatesCandidatesWithoutOutputChanges(params string[] raw)
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "rejected"));
        using var templater = await CreateTemplaterAsync(output);
        var original = (await templater.ResolveGroupAsync(ShortName, TemplateType.Item, CancellationToken.None)).Group!;
        var before = SnapshotProject();

        var results = original.Select(candidate => Parse(candidate, raw)).ToArray();
        var group = original.FilterByArguments(
            results.Where(result => result.IsArgumentCompatible).Select(result => result.TemplateIdentity).ToHashSet(StringComparer.Ordinal))
            .FilterToHighestPrecedence();

        group.Should().BeEmpty();
        results.Should().OnlyContain(result => !result.IsArgumentCompatible);
        results.Should().OnlyContain(result => result.InvalidInputDiagnostics.Count > 0);
        SnapshotProject().Should().BeEquivalentTo(before);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task Parse_NameAndRequiredDefault_UseSeparateNativeInputs()
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "default-control"));
        using var templater = await CreateTemplaterAsync(output);
        var selected = (await templater.ResolveGroupAsync(LowIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var parsed = Parse(selected, ["--greeting", "control", "-p:f", "false"]);

        var nativeMissing = await selected.InvokeAsync(new(output,
            new Dictionary<string, string?> { ["Greeting"] = "control", ["Feature"] = "false" }, "Control", DryRun: true), CancellationToken.None);
        var parserComplete = await selected.InvokeAsync(new(output, parsed.ValuesByCanonicalName, "Control", DryRun: true), CancellationToken.None);

        nativeMissing.Status.Should().Be(TemplateInvocationStatus.MissingRequiredParameters);
        parsed.ValuesByCanonicalName["AuthLevel"].Should().Be("anonymous");
        parserComplete.Status.Should().Be(TemplateInvocationStatus.Success);
        Path.GetFullPath(Path.Combine(output.Info.FullName, parserComplete.Changes.Should().ContainSingle().Which.Path))
            .Should().Be(Path.Combine(output.Info.FullName, "Control.txt"));
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task Parse_AfterPubliclyUnchangedRuntimeControls_DoesNotBypassTypeConstraintsOrLifetime()
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "controls"));
        var templater = await CreateTemplaterAsync(output);
        var wrongType = await templater.ResolveGroupAsync("Func.Tests.Parser.Project", TemplateType.Item, CancellationToken.None);
        var group = (await templater.ResolveGroupAsync(LowIdentity, TemplateType.Item, CancellationToken.None)).Group!;
        var selected = group[0];
        var parsed = Parse(selected, ["--greeting", "hello", "-p:f", "true"]);
        templater.Dispose();
        var request = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, "Disposed");

        Func<Task> invoke = () => selected.InvokeAsync(request, CancellationToken.None);

        wrongType.Status.Should().Be(TemplateResolutionStatus.WrongType);
        wrongType.Group.Should().BeNull();
        await invoke.Should().ThrowAsync<ObjectDisposedException>();
        using var restricted = await CreateTemplaterAsync(output, usable: false);
        var unavailable = await restricted.ResolveGroupAsync(LowIdentity, TemplateType.Item, CancellationToken.None);
        unavailable.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        unavailable.Group.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("4.35.0", true)]
    [InlineData("5.0.0", false)]
    public async Task Parse_ContextBundleEligibility_PrecedesArgumentCompatibility(string? bundleVersion, bool eligible)
    {
        await InstallAsync(requireBundle: true);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "bundle-control"));
        using var templater = await CreateTemplaterAsync(output, bundleVersion: bundleVersion);
        var before = SnapshotProject();

        var resolution = await templater.ResolveGroupAsync(LowIdentity, TemplateType.Item, CancellationToken.None);

        resolution.Status.Should().Be(eligible ? TemplateResolutionStatus.Success : TemplateResolutionStatus.Unavailable);
        SnapshotProject().Should().BeEquivalentTo(before);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        if (eligible)
        {
            var selected = resolution.Group.Should().ContainSingle().Subject;
            var parsed = Parse(selected, ["--greeting", "bundle control", "-p:f", "true"]);
            var preview = await selected.InvokeAsync(new(output, parsed.ValuesByCanonicalName, "Bundle", DryRun: true), CancellationToken.None);

            parsed.IsComplete.Should().BeTrue();
            preview.Status.Should().Be(TemplateInvocationStatus.Success);
            Path.GetFullPath(Path.Combine(output.Info.FullName, preview.Changes.Should().ContainSingle().Which.Path))
                .Should().Be(Path.Combine(output.Info.FullName, "Bundle.txt"));
            SnapshotProject().Should().BeEquivalentTo(before);
            Directory.Exists(output.Info.FullName).Should().BeFalse();
        }
        else
        {
            resolution.Group.Should().BeEmpty();
            resolution.Diagnostics.Should().ContainSingle().Which.ConstraintEvaluation.Diagnostics
                .Should().Contain(diagnostic => diagnostic.Type == "func-bundle");
        }
    }

    [Theory]
    [InlineData("bool", "TRUE", true)]
    [InlineData("boolean", "false", true)]
    [InlineData("int", "9223372036854775807", true)]
    [InlineData("integer", "-9223372036854775808", true)]
    [InlineData("float", "-1.25e3", true)]
    [InlineData("hex", "0xFFFFFFFFFFFFFFFF", true)]
    [InlineData("text", "--flag", true)]
    [InlineData("string", "", true)]
    [InlineData("bool", "yes", false)]
    [InlineData("int", "9223372036854775808", false)]
    [InlineData("float", "invalid", false)]
    [InlineData("hex", "FF", false)]
    public async Task Parse_ProjectedNativeScalar_AgreesWithNativePreflight(string type, string value, bool compatible)
    {
        await InstallAsync(type);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "scalar-control"));
        using var templater = await CreateTemplaterAsync(output);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Parser.Scalar", TemplateType.Item, CancellationToken.None)).Group![0];
        var raw = new[] { "--Scalar=" + value };
        var before = SnapshotProject();

        var result = Parse(selected, raw);
        var native = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["Scalar"] = value }, "Scalar", DryRun: true),
            CancellationToken.None);

        result.IsComplete.Should().Be(compatible);
        native.Status.Should().Be(compatible ? TemplateInvocationStatus.Success : TemplateInvocationStatus.InvalidArguments);
        SnapshotProject().Should().BeEquivalentTo(before);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private TemplateCandidateParseResult Parse(ResolvedTemplate template, IReadOnlyList<string> raw,
        IReadOnlyDictionary<string, string?>? completed = null)
        => _parser.Parse(new(template.Entry.Identity, template.Parameters, _reserved, raw, completed));

    private async Task<Templater> CreateTemplaterAsync(WorkingDirectory output, bool usable = true, string? bundleVersion = "4.35.0")
    {
        TemplateEngineContext context = new(output,
            new(WorkingDirectory.FromExplicit(Path.Combine(_root, "project")), "node", "JavaScript"),
            bundleVersion is null ? null : new("Microsoft.Azure.Functions.ExtensionBundle", bundleVersion));
        TemplateConstraintEvaluationSnapshot snapshot = new([new("node-package", NuGetVersion.Parse("1.0.0"), ["node"], usable)]);
        var store = new TemplateStore(new CliConfigurationPathsOptions(Path.Combine(_root, "home")),
            new PhysicalFileSystem(), new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        return await new TemplaterFactory(store).CreateAsync(context, snapshot, CancellationToken.None);
    }

    private Dictionary<string, string> SnapshotProject()
        => Directory.GetFiles(Path.Combine(_root, "project"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Path.Combine(_root, "project"), path), File.ReadAllText);

    private async Task InstallAsync(string scalarType = "string", bool requireBundle = false)
    {
        string package = Path.Combine(_root, "package");
        string low = Path.Combine(package, "low", ".template.config");
        string high = Path.Combine(package, "high", ".template.config");
        string project = Path.Combine(package, "project", ".template.config");
        string scalar = Path.Combine(package, "scalar", ".template.config");
        Directory.CreateDirectory(low);
        Directory.CreateDirectory(high);
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(scalar);
        File.WriteAllText(Path.Combine(low, "template.json"), """
            {
              "identity":"Func.Tests.Parser.Low","groupIdentity":"Func.Tests.Parser.Group",
              "name":"Parser low","shortName":"func-tests-parser","precedence":10,
              "tags":{"type":"item","language":"JavaScript"},"sourceName":"ItemFunction",
              "constraints":{"stack":{"type":"func-workload","args":"node"}},
              "symbols":{
                "Greeting":{"type":"parameter","datatype":"string","isRequired":true,"replaces":"GREETING"},
                "AuthLevel":{"type":"parameter","datatype":"choice","isRequired":true,"defaultValue":"anonymous",
                  "choices":[{"choice":"anonymous"},{"choice":"function"}],"replaces":"AUTH"},
                "Feature":{"type":"parameter","datatype":"bool","isRequired":true,"replaces":"FEATURE"},
                "Directory":{"type":"bind","binding":"host:WorkingDirectory","replaces":"OUTPUT"},
                "ProjectRoot":{"type":"bind","binding":"host:func:project-root","replaces":"ROOT"}
              }
            }
            """);
        if (requireBundle)
        {
            string templatePath = Path.Combine(low, "template.json");
            JsonNode configuration = JsonNode.Parse(File.ReadAllText(templatePath))!;
            configuration["constraints"]!["bundle"] = JsonNode.Parse("""
                {"type":"func-bundle","args":{"id":"Microsoft.Azure.Functions.ExtensionBundle","version":"[4.0.0,5.0.0)"}}
                """);
            File.WriteAllText(templatePath, configuration.ToJsonString());
        }

        File.WriteAllText(Path.Combine(low, "func.host.json"), """
            {"symbolInfo":{"Greeting":{"longName":"greeting"},"AuthLevel":{"longName":"output","shortName":"n"},
              "Feature":{"longName":"force","shortName":"f"}}}
            """);
        File.WriteAllText(Path.Combine(package, "low", "ItemFunction.txt"), "ItemFunction|GREETING|AUTH|FEATURE|OUTPUT|ROOT");
        File.WriteAllText(Path.Combine(high, "template.json"), """
            {
              "identity":"Func.Tests.Parser.High","groupIdentity":"Func.Tests.Parser.Group",
              "name":"Parser high","shortName":"func-tests-parser","precedence":100,
              "tags":{"type":"item","language":"JavaScript"},
              "symbols":{"Other":{"type":"parameter","datatype":"string","defaultValue":"other"}}
            }
            """);
        File.WriteAllText(Path.Combine(package, "high", "High.txt"), "not selected");
        File.WriteAllText(Path.Combine(project, "template.json"), """
            {"identity":"Func.Tests.Parser.Project","name":"Project","shortName":"func-tests-parser","tags":{"type":"project"}}
            """);
        File.WriteAllText(Path.Combine(package, "project", "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(scalar, "template.json"), JsonSerializer.Serialize(new
        {
            identity = "Func.Tests.Parser.Scalar",
            name = "Scalar control",
            shortName = "func-tests-parser-scalar",
            tags = new { type = "item" },
            symbols = new { Scalar = new { type = "parameter", datatype = scalarType, isRequired = true, replaces = "VALUE" } },
        }));
        File.WriteAllText(Path.Combine(package, "scalar", "Scalar.txt"), "VALUE");
        Directory.CreateDirectory(Path.Combine(_root, "project"));
        File.WriteAllText(Path.Combine(_root, "project", "host.json"), JsonSerializer.Serialize(new
        {
            version = "2.0",
            extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.0.0,5.0.0)" },
        }));
        await TemplateFixtures.InstallAsync(package, Path.Combine(_root, "home", "templates"), CancellationToken.None);
    }
}
