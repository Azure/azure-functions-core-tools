// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateMountedSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-mounted-schema-" + Guid.NewGuid().ToString("N"));

    private string Hive => Path.Combine(_root, "hive");

    [Theory]
    [InlineData(null, null)]
    [InlineData("0", null)]
    [InlineData("7123", null)]
    [InlineData(null, "0")]
    [InlineData(null, "7124")]
    public Task InvokeAsync_IntegerPort_PreservesOmissionAndExplicitOrAuthoredDefaults(string? explicitPort, string? authoredDefault)
        => InvokeIntegerPortAsync(explicitPort, authoredDefault, includeEmptyAdditionalConfigFiles: false);

    [Fact]
    public Task InvokeAsync_IntegerPort_EmptyAdditionalConfigFiles_PreservesGeneratedFallback()
        => InvokeIntegerPortAsync(null, null, includeEmptyAdditionalConfigFiles: true);

    private async Task InvokeIntegerPortAsync(string? explicitPort, string? authoredDefault, bool includeEmptyAdditionalConfigFiles)
    {
        string package = Path.Combine(_root, "port-template");
        string configurationDirectory = Path.Combine(package, ".template.config");
        Directory.CreateDirectory(configurationDirectory);
        JsonNode configuration = JsonNode.Parse("""
            {
              "identity":"Func.Tests.MountedPort","name":"Port default control","shortName":"func-tests-mounted-port",
              "tags":{"type":"item"},
              "symbols":{
                "FunctionsHttpPort":{"type":"parameter","datatype":"integer"},
                "GeneratedPort":{"type":"generated","generator":"port","parameters":{"low":7000,"high":7300}},
                "SelectedPort":{"type":"generated","generator":"coalesce",
                  "parameters":{"sourceVariableName":"FunctionsHttpPort","fallbackVariableName":"GeneratedPort"},"replaces":"PORT_VALUE"}
              }
            }
            """)!;
        if (authoredDefault is not null)
        {
            configuration["symbols"]!["FunctionsHttpPort"]!["defaultValue"] = authoredDefault;
        }
        if (includeEmptyAdditionalConfigFiles)
        {
            configuration["additionalConfigFiles"] = new JsonArray();
        }
        File.WriteAllText(Path.Combine(configurationDirectory, "template.json"), configuration.ToJsonString());
        File.WriteAllText(Path.Combine(package, "launchSettings.json"), """{"commandLineArgs":"--port PORT_VALUE"}""");
        await TemplateFixtures.InstallAsync(package, Hive);

        for (int flow = 0; flow < 2; flow++)
        {
            using Templater templater = CreateTemplater();
            var selected = (await templater.ResolveGroupAsync("Func.Tests.MountedPort", TemplateType.Item, CancellationToken.None)).Group![0];
            var definition = selected.Parameters.Should().ContainSingle().Subject;
            var values = selected.Parameters.Where(parameter => parameter.DefaultValue is not null)
                .ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue, StringComparer.Ordinal);
            if (explicitPort is null && authoredDefault is null)
            {
                definition.DefaultValue.Should().BeNull();
                values.Should().NotContainKey("FunctionsHttpPort");
            }
            if (explicitPort is not null)
            {
                values["FunctionsHttpPort"] = explicitPort;
            }
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "port-output-" + flow));
            var request = new TemplateInvocationRequest(output, values, "Port");

            var preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);
            Directory.Exists(output.Info.FullName).Should().BeFalse();
            var created = await selected.InvokeAsync(request, CancellationToken.None);

            preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
            created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
            using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Info.FullName, "launchSettings.json")));
            string arguments = launch.RootElement.GetProperty("commandLineArgs").GetString()!;
            if ((explicitPort ?? authoredDefault) is { } expected)
            {
                arguments.Should().Be("--port " + expected);
                values.Should().Contain("FunctionsHttpPort", expected);
                if (authoredDefault is not null)
                {
                    definition.DefaultValue.Should().Be(authoredDefault);
                }
            }
            else
            {
                int port = int.Parse(arguments["--port ".Length..], System.Globalization.CultureInfo.InvariantCulture);
                port.Should().BeInRange(7000, 7300);
                definition.DefaultValue.Should().BeNull();
                values.Should().NotContainKey("FunctionsHttpPort");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_FolderParameterRenamedBetweenFlows_UsesMountedSchema(bool useCurrentName)
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        await TemplateFixtures.InstallAsync(package, Hive);
        using (Templater first = CreateTemplater())
        {
            var selected = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            selected.Parameters.Should().ContainSingle().Which.CanonicalName.Should().Be("Greeting");
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "first-output"));
            var created = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["Greeting"] = "first-value" }, "First"), CancellationToken.None);
            created.Status.Should().Be(TemplateInvocationStatus.Success);
            File.ReadAllText(Path.Combine(output.Info.FullName, "First.txt")).Should().Be("First first-value");
        }

        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration)
            .Replace("\"Greeting\"", "\"Salutation\"", StringComparison.Ordinal)
            .Replace("\"hello\"", "\"updated-default\"", StringComparison.Ordinal));
        using Templater second = CreateTemplater();
        var current = (await second.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
        var destination = WorkingDirectory.FromExplicit(Path.Combine(_root, "second-output"));
        var values = new Dictionary<string, string?> { [useCurrentName ? "Salutation" : "Greeting"] = "user-value" };

        var result = await current.InvokeAsync(new(destination, values, "Second"), CancellationToken.None);

        if (useCurrentName)
        {
            result.Status.Should().Be(TemplateInvocationStatus.Success);
            File.ReadAllText(Path.Combine(destination.Info.FullName, "Second.txt")).Should().Be("Second user-value");
        }
        else
        {
            string? actual = File.Exists(Path.Combine(destination.Info.FullName, "Second.txt"))
                ? File.ReadAllText(Path.Combine(destination.Info.FullName, "Second.txt"))
                : null;
            result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments, $"the removed parameter must not be ignored; actual output was '{actual}'");
            Directory.Exists(destination.Info.FullName).Should().BeFalse();
        }

        current.Parameters.Should().ContainSingle().Which.CanonicalName.Should().Be("Salutation");
        current.Parameters[0].DefaultValue.Should().Be("updated-default");
    }

    [Fact]
    public async Task InvokeAsync_FolderChoiceAndDefaultChangedBetweenFlows_UsesCurrentContract()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace(
            "\"datatype\": \"string\"",
            """ "datatype": "choice", "choices": [{"choice":"hello"},{"choice":"old"}] """,
            StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, Hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), Hive);
        using (Templater first = CreateTemplater())
        {
            var selected = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            selected.Parameters.Should().ContainSingle().Which.Choices.Select(choice => choice.Value).Should().Equal("hello", "old");
        }

        File.WriteAllText(configuration, File.ReadAllText(configuration)
            .Replace("\"hello\"", "\"updated-default\"", StringComparison.Ordinal)
            .Replace("\"old\"", "\"new-choice\"", StringComparison.Ordinal));
        using Templater second = CreateTemplater();
        var current = (await second.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "choice-output"));
        var defaultOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "default-output"));
        var rejectedOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "rejected-output"));

        var created = await current.InvokeAsync(new(output, new Dictionary<string, string?> { ["Greeting"] = "new-choice" }, "Choice"), CancellationToken.None);
        var defaulted = await current.InvokeAsync(new(defaultOutput, new Dictionary<string, string?>(), "Default"), CancellationToken.None);
        var rejected = await current.InvokeAsync(new(rejectedOutput, new Dictionary<string, string?> { ["Greeting"] = "old" }, "Rejected"), CancellationToken.None);

        current.Parameters.Should().ContainSingle().Which.Choices.Select(choice => choice.Value).Should().Equal("updated-default", "new-choice");
        current.Parameters[0].DefaultValue.Should().Be("updated-default");
        created.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Choice.txt")).Should().Be("Choice new-choice");
        defaulted.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(defaultOutput.Info.FullName, "Default.txt")).Should().Be("Default updated-default");
        rejected.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        Directory.Exists(rejectedOutput.Info.FullName).Should().BeFalse();

        var healthy = (await second.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var healthyOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "healthy-output"));
        var healthyResult = await healthy.InvokeAsync(new(healthyOutput, new Dictionary<string, string?>(), "Healthy"), CancellationToken.None);
        healthyResult.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(healthyOutput.Info.FullName, "Healthy.txt")).Should().Be("Healthy content");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_FolderHostAliasesChangedBetweenFlows_UsesMountedMetadata(bool hadHostFile)
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        var hostFile = Path.Combine(package, "item", ".template.config", "func.host.json");
        if (hadHostFile)
        {
            File.WriteAllText(hostFile, """{"symbolInfo":{"Greeting":{"longName":"old-greeting","shortName":"g"}}}""");
        }

        await TemplateFixtures.InstallAsync(package, Hive);
        using (Templater first = CreateTemplater())
        {
            var selected = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            selected.Parameters.Should().ContainSingle().Which.LongName.Should().Be(hadHostFile ? "old-greeting" : "Greeting");
        }

        File.WriteAllText(hostFile, """{"symbolInfo":{"Greeting":{"longName":"current-greeting","shortName":"s"}}}""");
        using Templater second = CreateTemplater();
        var current = (await second.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "alias-output"));

        var result = await current.InvokeAsync(new(output, new Dictionary<string, string?> { ["Greeting"] = "user-value" }, "Alias"), CancellationToken.None);

        current.Parameters.Should().ContainSingle().Which.LongName.Should().Be("current-greeting");
        current.Parameters[0].ShortName.Should().Be("s");
        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Alias.txt")).Should().Be("Alias user-value");

        File.AppendAllText(hostFile, " ");
        var changedOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "changed-host-output"));
        var changed = await current.InvokeAsync(new(changedOutput, new Dictionary<string, string?>(), "Changed"), CancellationToken.None);
        changed.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        Directory.Exists(changedOutput.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_FolderParameterBecomesRequiredInteger_UsesMountedValidation()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        await TemplateFixtures.InstallAsync(package, Hive);
        using (Templater first = CreateTemplater())
        {
            var selected = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            selected.Parameters.Should().ContainSingle().Which.DataType.Should().Be("string");
            selected.Parameters[0].IsRequired.Should().BeFalse();
        }

        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace(
            "\"datatype\": \"string\", \"defaultValue\": \"hello\"",
            "\"datatype\": \"int\", \"isRequired\": true",
            StringComparison.Ordinal));
        using Templater second = CreateTemplater();
        var current = (await second.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
        var validOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "integer-output"));
        var invalidOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "invalid-integer-output"));
        var missingOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "missing-integer-output"));

        var valid = await current.InvokeAsync(new(validOutput, new Dictionary<string, string?> { ["Greeting"] = "42" }, "Integer"), CancellationToken.None);
        var invalid = await current.InvokeAsync(new(invalidOutput, new Dictionary<string, string?> { ["Greeting"] = "text" }, "Invalid"), CancellationToken.None);
        var missing = await current.InvokeAsync(new(missingOutput, new Dictionary<string, string?>(), "Missing"), CancellationToken.None);

        current.Parameters.Should().ContainSingle().Which.DataType.Should().Be("int");
        current.Parameters[0].IsRequired.Should().BeTrue();
        valid.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(validOutput.Info.FullName, "Integer.txt")).Should().Be("Integer 42");
        invalid.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        missing.Status.Should().Be(TemplateInvocationStatus.MissingRequiredParameters);
        Directory.Exists(invalidOutput.Info.FullName).Should().BeFalse();
        Directory.Exists(missingOutput.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnchangedArchiveSchema_RemainsUsableAcrossFlows()
    {
        var archive = TemplateFixtures.WriteSharedJourneyPackage(_root, shared: false);
        var rawFingerprint = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(_root, "journey-templates", "item", ".template.config", "template.json"))));
        await TemplateFixtures.InstallAsync(archive, Hive);
        for (int index = 0; index < 2; index++)
        {
            var session = new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive,
                new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));
            using Templater flow = TemplateFixtures.CreateTemplater(session);
            var selected = (await flow.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            selected.Parameters.Should().ContainSingle().Which.CanonicalName.Should().Be("Greeting");
            selected.Parameters[0].DefaultValue.Should().Be("hello");
            var descriptor = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
            var snapshot = await session.ReadConfigurationAsync(descriptor, CancellationToken.None);
            snapshot.Fingerprint.Should().Be(rawFingerprint);
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "archive-output-" + index));

            var created = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["Greeting"] = "user-value" }, "Archive"), CancellationToken.None);

            created.Status.Should().Be(TemplateInvocationStatus.Success);
            File.ReadAllText(Path.Combine(output.Info.FullName, "Archive.txt")).Should().Be("Archive user-value");
        }
    }

    [Fact]
    public async Task ReadConfigurationAsync_HostChange_PreservesRawTemplateFingerprint()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        var hostFile = Path.Combine(package, "item", ".template.config", "func.host.json");
        File.WriteAllText(hostFile, """{"symbolInfo":{"Greeting":{"longName":"before"}}}""");
        await TemplateFixtures.InstallAsync(package, Hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        var before = await session.ReadConfigurationAsync(template, CancellationToken.None);
        File.WriteAllText(hostFile, """{"symbolInfo":{"Greeting":{"longName":"after"}}}""");

        var after = await session.ReadConfigurationAsync(template, CancellationToken.None);

        var rawFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(configuration)));
        before.Fingerprint.Should().Be(rawFingerprint);
        after.Fingerprint.Should().Be(rawFingerprint);
        after.HostData.Should().NotBe(before.HostData);
        after.HostData.Should().Be(File.ReadAllText(hostFile));
    }

    [Fact]
    public async Task InvokeAsync_FolderHostRemovedBetweenFlows_DoesNotReuseCachedAliases()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        var hostFile = Path.Combine(package, "item", ".template.config", "func.host.json");
        File.WriteAllText(hostFile, """{"symbolInfo":{"Greeting":{"longName":"old-greeting","shortName":"g"}}}""");
        await TemplateFixtures.InstallAsync(package, Hive);
        using (Templater first = CreateTemplater())
        {
            var selected = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            selected.Parameters.Should().ContainSingle().Which.LongName.Should().Be("old-greeting");
            File.Delete(hostFile);
            var staleOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "stale-host-output"));
            var stale = await selected.InvokeAsync(new(staleOutput, new Dictionary<string, string?>(), "Stale"), CancellationToken.None);
            stale.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
            Directory.Exists(staleOutput.Info.FullName).Should().BeFalse();
        }

        using Templater second = CreateTemplater();
        var current = (await second.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "removed-host-output"));

        var created = await current.InvokeAsync(new(output, new Dictionary<string, string?> { ["Greeting"] = "user-value" }, "Removed"), CancellationToken.None);

        current.Parameters.Should().ContainSingle().Which.LongName.Should().Be("Greeting");
        current.Parameters[0].ShortName.Should().BeNull();
        created.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Removed.txt")).Should().Be("Removed user-value");
    }

    [Fact]
    public async Task ResolveGroupAsync_FolderHostMalformedBetweenFlows_RemainsUnavailable()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        var hostFile = Path.Combine(package, "item", ".template.config", "func.host.json");
        File.WriteAllText(hostFile, """{"symbolInfo":{"Greeting":{"longName":"old-greeting"}}}""");
        await TemplateFixtures.InstallAsync(package, Hive);
        using (Templater first = CreateTemplater())
        {
            var selected = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            selected.Parameters.Should().ContainSingle().Which.LongName.Should().Be("old-greeting");
        }

        File.WriteAllText(hostFile, "{ invalid host JSON");
        using Templater second = CreateTemplater();

        var result = await second.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        result.Group.Should().BeEmpty();
        var diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        (diagnostic.MetadataDiagnostic ?? string.Join(" ", diagnostic.ConstraintEvaluation.Diagnostics.Select(item => item.Message)))
            .Should().Contain("func.host.json");
    }

    [Fact]
    public async Task InvokeAsync_FolderHostAddedAfterSelection_RequiresNewSelection()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        await TemplateFixtures.InstallAsync(package, Hive);
        using Templater flow = CreateTemplater();
        var selected = (await flow.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
        File.WriteAllText(Path.Combine(package, "item", ".template.config", "func.host.json"),
            """{"symbolInfo":{"Greeting":{"longName":"new-greeting"}}}""");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "added-host-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Added"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Templater CreateTemplater()
        => TemplateFixtures.CreateTemplater(new(new(WorkingDirectory.FromExplicit(_root)), Hive,
            new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)])));
}
