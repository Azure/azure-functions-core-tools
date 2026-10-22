// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.V2;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public void CatalogPack_CompleteOwnedInventory_PreservesIdentitiesHiddenPolicyParametersAndBindingRequirements()
    {
        var legacy = ReadNodeSource();
        using var inner = ReadNodeArchive();
        using var bindings = JsonDocument.Parse(File.ReadAllText(Path.Combine(NodeSourceDirectory(), "templates", "_bindings.json")));
        var paths = inner.GetFiles().Where(path => path.EndsWith("/.template.config/template.json", StringComparison.Ordinal)).ToArray();

        paths.Should().HaveCount(legacy.Templates.Count + 2);
        foreach (var template in legacy.Templates)
        {
            var expected = V2TemplateProjection.Project(template, legacy, "node")!;
            string path = NodeConfigurationPath(template.Id!);
            using var config = JsonDocument.Parse(Read(inner, path));
            var root = config.RootElement;
            root.GetProperty("identity").GetString().Should().Be(NativeNodeIdentity(template.Id!));
            root.GetProperty("name").GetString().Should().Be(expected.DisplayName);
            root.GetProperty("tags").GetProperty("language").GetString().Should()
                .Be(template.Language!.Equals("javascript", StringComparison.OrdinalIgnoreCase) ? "JavaScript" : "TypeScript");
            root.GetProperty("defaultName").GetString().Should().Be(expected.DefaultFunctionName);
            root.GetProperty("shortName").EnumerateArray().Select(alias => alias.GetString()).Should()
                .Contain(template.Id!, expected.DisplayName);
            using var host = JsonDocument.Parse(Read(inner, path.Replace("template.json", "func.host.json", StringComparison.Ordinal)));
            var metadata = FuncHostTemplateMetadataReader.Read("func.host.json", host.RootElement.GetRawText());
            metadata.IsHidden.Should().Be(V2HiddenTemplates.IsHidden(template.Id!));
            if (template.Id!.StartsWith("HttpTrigger-", StringComparison.Ordinal))
            {
                continue; // HTTP keeps its pre-existing native authorization parameter.
            }

            var symbols = root.GetProperty("symbols");
            var prompts = expected.Metadata.UserPrompts.Where(prompt => prompt.Id != "trigger-functionName").ToArray();
            symbols.EnumerateObject().Where(symbol => symbol.Value.GetProperty("type").GetString() == "parameter")
                .Select(symbol => symbol.Name).Should().BeEquivalentTo(prompts.Select(prompt => prompt.Id));
            var hydrated = new TemplateOptionHydrator([]).HydrateWithIds(expected);
            foreach (var prompt in prompts)
            {
                var symbol = symbols.GetProperty(prompt.Id);
                symbol.GetProperty("datatype").GetString().Should().Be(prompt.DataType);
                symbol.GetProperty("defaultValue").GetString().Should().Be(prompt.DefaultValue);
                symbol.GetProperty("isRequired").GetBoolean().Should().Be(prompt.IsRequired);
                symbol.GetProperty("description").GetString().Should().Be(prompt.Description);
                metadata.Symbols[prompt.Id].LongName.Should()
                    .Be(hydrated.Single(option => option.PromptId == prompt.Id).Option.Name.TrimStart('-'));
                if (prompt.Choices is not null)
                {
                    symbol.GetProperty("choices").EnumerateArray().Select(choice => choice.GetProperty("choice").GetString())
                        .Should().Equal(prompt.Choices);
                }
            }

            var required = bindings.RootElement.GetProperty(template.Id!).EnumerateArray().Select(binding => binding.GetString()).ToArray();
            var constraints = root.GetProperty("constraints");
            if (required.Length == 0)
            {
                constraints.TryGetProperty("bindings", out _).Should().BeFalse();
            }
            else
            {
                constraints.GetProperty("bindings").GetProperty("type").GetString().Should().Be("func-bundle-bindings");
                constraints.GetProperty("bindings").GetProperty("args").EnumerateArray().Select(binding => binding.GetString())
                    .Should().Equal(required);
                constraints.GetProperty("bundle").GetProperty("args").EnumerateArray().Select(argument => argument.GetProperty("id").GetString())
                    .Should().BeEquivalentTo("Microsoft.Azure.Functions.ExtensionBundle", "Microsoft.Azure.Functions.ExtensionBundle.Preview",
                        "Microsoft.Azure.Functions.ExtensionBundle.Experimental");
            }
        }
    }

    [Fact]
    public async Task CatalogInstall_ActualPhysicalWorkload_ReopensCompleteNativeMetadataWithoutLosingEmptyDefaults()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var legacy = ReadNodeSource();
        using var services = await CreateServicesAsync();
        var stack = services.GetServices<IProjectStack>().Single(stack => stack.Stack == "node");
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        using var templater = new Templater(session);

        var items = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        items.Select(item => item.Identity).Should().BeEquivalentTo(legacy.Templates.Select(template => NativeNodeIdentity(template.Id!)));
        items.Should().OnlyContain(item => item.MetadataDiagnostic == null);
        foreach (var template in legacy.Templates)
        {
            var item = items.Single(item => item.Identity == NativeNodeIdentity(template.Id!));
            stack.SupportedLanguages.Should().Contain(item.Language);
            var byId = await templater.ResolveGroupAsync(template.Id!, TemplateType.Item, CancellationToken.None);
            byId.Status.Should().BeOneOf(TemplateResolutionStatus.Success, TemplateResolutionStatus.Unavailable);
            byId.Group!.Select(candidate => candidate.Entry.Identity).Concat(byId.Diagnostics.Select(candidate => candidate.Identity))
                .Should().Contain(item.Identity);
            var byDisplayName = await templater.ResolveGroupAsync(item.Name, TemplateType.Item, CancellationToken.None);
            byDisplayName.Status.Should().BeOneOf(TemplateResolutionStatus.Success, TemplateResolutionStatus.Unavailable);
            byDisplayName.Group!.Select(candidate => candidate.Entry.Identity).Concat(byDisplayName.Diagnostics.Select(candidate => candidate.Identity))
                .Should().Contain(item.Identity);
        }
        foreach (var template in legacy.Templates.Where(template => !template.Id!.StartsWith("HttpTrigger-", StringComparison.Ordinal)))
        {
            var expected = V2TemplateProjection.Project(template, legacy, "node")!;
            var item = items.Single(item => item.Identity == template.Id);
            item.IsHidden.Should().Be(V2HiddenTemplates.IsHidden(template.Id!));
            foreach (var prompt in expected.Metadata.UserPrompts.Where(prompt => prompt.Id != "trigger-functionName"))
            {
                var parameter = item.Parameters.Single(parameter => parameter.CanonicalName == prompt.Id);
                parameter.DefaultValue.Should().Be(prompt.DefaultValue);
                parameter.IsRequired.Should().Be(prompt.IsRequired);
                parameter.Choices.Select(choice => choice.Value).Should().Equal(prompt.Choices ?? []);
            }
            var parsed = new TemplateArgumentParser().Parse(new(item.Identity, item.Parameters, new HashSet<string>(), []), CancellationToken.None);
            parsed.IsComplete.Should().BeTrue(string.Join("; ", parsed.DefinitionDiagnostics));
        }
    }

    [Fact]
    public async Task CatalogNativeBackend_EveryOwnedItem_PreviewsAndCreatesSourceEquivalentFilesWithoutChangingExistingProject()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var legacy = ReadNodeSource();
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var nativeTemplates = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        using var templater = new Templater(session);
        var items = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        var creator = new TemplateCreator(session.Settings);
        var index = 0;
        foreach (var template in legacy.Templates)
        {
            var native = nativeTemplates.Single(item => item.Identity == NativeNodeIdentity(template.Id!));
            var item = items.Single(item => item.Identity == native.Identity);
            string project = Directory.CreateDirectory(Path.Combine(_root, "render-" + index++)).FullName;
            string target = Directory.CreateDirectory(Path.Combine(project, "src", "functions")).FullName;
            var preserved = new Dictionary<string, string>
            {
                [Path.Combine(project, "host.json")] = """{"version":"2.0","custom":"preserve"}""",
                [Path.Combine(project, "package.json")] = """{"name":"existing","dependencies":{"custom":"1.0.0"}}""",
                [Path.Combine(target, "existing.js")] = "// existing user source\n",
            };
            foreach (var file in preserved)
            {
                File.WriteAllText(file.Key, file.Value);
            }
            var values = item.Parameters.ToDictionary(parameter => parameter.CanonicalName,
                parameter => (string?)(parameter.DataType == "choice" ? parameter.Choices.Last().Value : "custom-setting"),
                StringComparer.Ordinal);
            string name = "MigratedFunction";

            // This authoring check uses the real native backend, not a success-shaped fake.
            // Production eligibility is tested separately and stays fail-closed for unsupported constraints.
            var preview = await creator.InstantiateAsync(native, name, name, target, values, dryRun: true, cancellationToken: CancellationToken.None);
            preview.Status.Should().Be(CreationResultStatus.Success, template.Id);
            Directory.GetFiles(target).Should().ContainSingle();
            var created = await creator.InstantiateAsync(native, name, name, target, values, cancellationToken: CancellationToken.None);

            created.Status.Should().Be(CreationResultStatus.Success, template.Id);
            string extension = template.Language!.Equals("javascript", StringComparison.OrdinalIgnoreCase) ? ".js" : ".ts";
            string output = Path.Combine(target, name + extension);
            string source = template.Files!.Single().Value;
            string expected = source.Replace("$(FUNCTION_NAME_INPUT)", name, StringComparison.Ordinal);
            if (template.Id!.StartsWith("HttpTrigger-", StringComparison.Ordinal))
            {
                expected = expected.Replace("authLevel: 'anonymous'", "authLevel: 'admin'", StringComparison.Ordinal);
            }
            File.ReadAllText(output).ReplaceLineEndings("\n").Should().Be(expected.ReplaceLineEndings("\n"), template.Id);
            created.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath).Should()
                .BeEquivalentTo(preview.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath));
            foreach (var file in preserved)
            {
                File.ReadAllText(file.Key).Should().Be(file.Value);
            }
            Directory.GetFiles(project, "function.json", SearchOption.AllDirectories).Should().BeEmpty();
            Directory.GetFiles(target).Should().HaveCount(2);

            var defaults = new TemplateArgumentParser().Parse(new(item.Identity, item.Parameters, new HashSet<string>(), []), CancellationToken.None);
            defaults.IsComplete.Should().BeTrue();
            string defaultTarget = Path.Combine(project, "default-output");
            var defaultCreated = await creator.InstantiateAsync(native, null, null, defaultTarget,
                defaults.ValuesByCanonicalName, cancellationToken: CancellationToken.None);
            defaultCreated.Status.Should().Be(CreationResultStatus.Success, template.Id);
            string defaultName = V2TemplateProjection.Project(template, legacy, "node")!.DefaultFunctionName!;
            string defaultExpected = source.Replace("$(FUNCTION_NAME_INPUT)", defaultName, StringComparison.Ordinal);
            File.ReadAllText(Path.Combine(defaultTarget, defaultName + extension)).ReplaceLineEndings("\n")
                .Should().Be(defaultExpected.ReplaceLineEndings("\n"), template.Id);
        }
    }

    [Theory]
    [InlineData("JavaScript", "TimerTrigger-JavaScript", ".js")]
    [InlineData("TypeScript", "TimerTrigger-TypeScript", ".ts")]
    public async Task CatalogFacade_BuiltInItem_UsesLegacyAliasAndRefusesConflictsWithoutChangingUserSource(
        string language, string identity, string extension)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;
        string target = Directory.CreateDirectory(Path.Combine(project, "src", "functions")).FullName;
        string existing = Path.Combine(target, "Existing" + extension);
        File.WriteAllText(existing, "// preserved user implementation");
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(target),
            new(WorkingDirectory.FromExplicit(project), "node", language));
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0"), ["node"], true)]);
        using var session = new TemplateEngineSession(context, Path.Combine(Home, "templates"), availability);
        using var templater = new Templater(session);
        var resolution = await templater.ResolveGroupAsync(identity, TemplateType.Item, CancellationToken.None);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        var selected = resolution.Group!.Single();
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Entry.Parameters, new HashSet<string>(),
            ["--timer-trigger-schedule", "0 0 * * * *"]), CancellationToken.None);
        parsed.IsComplete.Should().BeTrue();
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(target), parsed.ValuesByCanonicalName, "Existing");

        var conflict = await selected.InvokeAsync(request, CancellationToken.None);
        conflict.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.ReadAllText(existing).Should().Be("// preserved user implementation");
        var preview = await selected.InvokeAsync(request with { Name = "NewTimer", DryRun = true }, CancellationToken.None);
        File.Exists(Path.Combine(target, "NewTimer" + extension)).Should().BeFalse();
        var created = await selected.InvokeAsync(request with { Name = "NewTimer" }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Changes.Should().BeEquivalentTo(preview.Changes);
        File.ReadAllText(existing).Should().Be("// preserved user implementation");
        // The original owned payload ignores SCHEDULE_INPUT; migration must not silently change its semantics.
        File.ReadAllText(Path.Combine(target, "NewTimer" + extension)).Should().Contain("schedule: '0 */5 * * * *'");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("wrong-bundle", "4.35.0")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "3.0.0")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "4.35.0")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Preview", "4.35.0")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Experimental", "4.35.0")]
    public async Task CatalogEligibility_UnqualifiedBindingConstraint_FailsClosedRatherThanInventingChannelSupport(string? bundleId, string? version)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0"), ["node"], true)]);
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(_root),
            bundle: bundleId is null ? null : new(bundleId, version!));
        using var session = new TemplateEngineSession(context, Path.Combine(Home, "templates"), snapshot);
        using var templater = new Templater(session);

        var resolution = await templater.ResolveGroupAsync("QueueTrigger-JavaScript", TemplateType.Item, CancellationToken.None);

        resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        var diagnostic = resolution.Diagnostics.Single();
        diagnostic.ConstraintEvaluation.Status.Should().NotBe(TemplateConstraintEvaluationStatus.Eligible);
        diagnostic.ConstraintEvaluation.Diagnostics.Should().Contain(item => item.Type == "func-bundle-bindings"
            && item.Status == TemplateConstraintEvaluationStatus.NotEvaluated);
        if (bundleId is null or "wrong-bundle" || version == "3.0.0")
        {
            diagnostic.ConstraintEvaluation.Diagnostics.Should().Contain(item => item.Type == "func-bundle"
                && item.Status == TemplateConstraintEvaluationStatus.Restricted);
        }
        Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
    }

    private static V2Payload ReadNodeSource()
    {
        string source = NodeSourceDirectory();
        var templates = JsonSerializer.Deserialize<NewTemplate[]>(File.ReadAllText(Path.Combine(source, "templates", "templates.json")))!;
        var prompts = JsonSerializer.Deserialize<UserPromptDoc[]>(File.ReadAllText(Path.Combine(source, "bindings", "userPrompts.json")))!;
        var resources = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(source, "resources", "Resources.json")))!;
        return new(source, templates, prompts.ToDictionary(prompt => prompt.Id!, StringComparer.OrdinalIgnoreCase), resources);
    }

    private static string NodeSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string source = Path.Combine(directory.FullName, "src", "Workloads", "Templates", "Node", "content", "v2");
            if (Directory.Exists(source))
            {
                return source;
            }
        }
        throw new DirectoryNotFoundException("The owned Node catalog source is required for source-parity tests.");
    }

    private static string NativeNodeIdentity(string legacyId) => legacyId switch
    {
        "HttpTrigger-JavaScript" => "Azure.Functions.Node.Http.JavaScript",
        "HttpTrigger-TypeScript" => "Azure.Functions.Node.Http.TypeScript",
        _ => legacyId,
    };

    private static string NodeConfigurationPath(string legacyId) => legacyId switch
    {
        "HttpTrigger-JavaScript" => "content/http-js/.template.config/template.json",
        "HttpTrigger-TypeScript" => "content/http-ts/.template.config/template.json",
        _ => $"content/items/{legacyId}/.template.config/template.json",
    };

    private static PackageArchiveReader ReadNodeArchive()
    {
        using var outer = new PackageArchiveReader(BuiltArchive);
        var bytes = new MemoryStream();
        using var input = outer.GetStream("tools/any/templates/node-basic.nupkg");
        input.CopyTo(bytes);
        bytes.Position = 0;
        return new(bytes);
    }
}
