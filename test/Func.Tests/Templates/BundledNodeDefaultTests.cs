// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Node;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("EventHubTrigger-JavaScript", "eventHubTrigger-name", "--event-hub-trigger-name", "eventHubName", "samples-workitems")]
    [InlineData("ServiceBusQueueTrigger-JavaScript", "serviceBusTrigger-name", "--service-bus-trigger-name", "queueName", "myinputqueue")]
    [InlineData("ServiceBusTopicTrigger-JavaScript", "serviceBusTrigger-topic", "--service-bus-trigger-topic", "topicName", "mytopic")]
    [InlineData("ServiceBusTopicTrigger-JavaScript", "serviceBusTrigger-subscription", "--service-bus-trigger-subscription",
        "subscriptionName", "mysubscription")]
    [InlineData("EventHubTrigger-TypeScript", "eventHubTrigger-name", "--event-hub-trigger-name", "eventHubName", "samples-workitems")]
    [InlineData("ServiceBusQueueTrigger-TypeScript", "serviceBusTrigger-name", "--service-bus-trigger-name", "queueName", "myinputqueue")]
    [InlineData("ServiceBusTopicTrigger-TypeScript", "serviceBusTrigger-topic", "--service-bus-trigger-topic", "topicName", "mytopic")]
    [InlineData("ServiceBusTopicTrigger-TypeScript", "serviceBusTrigger-subscription", "--service-bus-trigger-subscription",
        "subscriptionName", "mysubscription")]
    public async Task Defaults_ApprovedEntityNames_PreserveOmittedValuesAndKeepExplicitIncludingEmptyOperative(
        string identity, string canonical, string option, string property, string omitted)
    {
        RecordingNodeSourceAuthority authority = new();
        NodeItemSource qualified = await ReadQualifiedSourceAsync(identity, authority);
        authority.Calls.Should().Be(1);
        qualified.HasHostSourceQualification.Should().Be(identity.EndsWith("-JavaScript", StringComparison.Ordinal));
        using var bridge = await CreateNodeBridgeAsync();
        string project = Path.Combine(_root, "defaults-project");
        string extension = identity.EndsWith("-TypeScript", StringComparison.Ordinal) ? "ts" : "js";
        await CreateNodeBridgeProjectAsync(bridge, project, extension);
        string target = Path.Combine(project, "src", "functions");
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(target)), CancellationToken.None);
        using var templater = new Templater(session);
        var entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single(item => item.Identity == identity);
        entry.Parameters.Single(parameter => parameter.CanonicalName == canonical).DefaultValue.Should().Be(omitted);
        var help = new NativeTemplateHelpProjector(new TemplateArgumentParser()).Project(entry, new HashSet<string>(), CancellationToken.None);
        help.HydratedOptions.Single(item => item.Name == option).Description.Should().Contain("Default: " + omitted + ".");

        foreach (var (label, value) in new[] { ("Omitted", (string?)null), ("Explicit", "parity-entity"), ("Empty", "") })
        {
            string name = property + label;
            string[] values = value is null ? [] : [option, value];
            string[] tokens = [target, "-t", identity, "--name", name, "--non-interactive", "--output", "json", .. values];

            (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(), tokens, dryRun: true)))
                .Should().Be(0, _interaction.AllOutput);
            File.Exists(Path.Combine(target, name + "." + extension)).Should().BeFalse();
            (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(), tokens)))
                .Should().Be(0, _interaction.AllOutput);

            var result = bridge.Runner.Last!;
            result.ArgumentResults.Single().ValuesByCanonicalName[canonical].Should().Be(value ?? omitted);
            result.ArgumentResults.Single().MissingRequiredParameters.Should().BeEmpty();
            string content = File.ReadAllText(Path.Combine(target, name + "." + extension));
            content.Should().Contain($"{property}: '{value ?? omitted}',");
            if (identity == "EventHubTrigger-TypeScript")
            {
                content.Should().Contain("connection: '%connection%',");
            }
            else if (identity != "EventHubTrigger-JavaScript")
            {
                content.Should().Contain("connection: '',");
            }
        }
    }

    [Theory]
    [InlineData("missingDescriptor")]
    [InlineData("missingSource")]
    [InlineData("sourceHash")]
    [InlineData("foreignSourcePath")]
    [InlineData("otherIdentity")]
    [InlineData("originalDefault")]
    [InlineData("nativeDefault")]
    [InlineData("unknownParameter")]
    [InlineData("duplicateParameter")]
    [InlineData("requiredFlag")]
    [InlineData("noDescriptorOriginalDefaults", true)]
    [InlineData("missingDescriptor", false, "TypeScript")]
    [InlineData("missingSource", false, "TypeScript")]
    [InlineData("sourceHash", false, "TypeScript")]
    [InlineData("foreignSourcePath", false, "TypeScript")]
    [InlineData("otherIdentity", false, "TypeScript")]
    [InlineData("originalDefault", false, "TypeScript")]
    [InlineData("nativeDefault", false, "TypeScript")]
    [InlineData("unknownParameter", false, "TypeScript")]
    [InlineData("duplicateParameter", false, "TypeScript")]
    [InlineData("requiredFlag", false, "TypeScript")]
    [InlineData("noDescriptorOriginalDefaults", true, "TypeScript")]
    public async Task Defaults_SelectedSource_RequiresExactOriginalAndNativeAssociations(
        string corruption, bool succeeds = false, string language = "JavaScript")
    {
        string identity = "ServiceBusQueueTrigger-" + language;
        const string canonical = "serviceBusTrigger-name";
        const string path = "provenance/node/native-defaults.json";
        string archive = Path.Combine(_root, "default-association.nupkg");
        using (var outer = new PackageArchiveReader(BuiltArchive))
        using (var output = File.Create(archive))
        {
            outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(output);
        }
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            JsonNode manifest = ReadJson(NodeItemCompatibility.ManifestPath);
            var mapping = manifest["templates"]!.AsArray().Single(item => item!["identity"]!.GetValue<string>() == identity)!;
            var defaults = ReadJson(path);
            if (corruption == "missingDescriptor") mapping.AsObject().Remove("nativeDefaults");
            else if (corruption == "missingSource") zip.GetEntry(path)!.Delete();
            else if (corruption == "sourceHash") mapping["nativeDefaults"]!["sha256"] = new string('0', 64);
            else if (corruption == "foreignSourcePath") mapping["nativeDefaults"]!["path"] = "provenance/node/other-defaults.json";
            else if (corruption is "requiredFlag" or "noDescriptorOriginalDefaults")
            {
                string configurationPath = mapping["configuration"]!["path"]!.GetValue<string>();
                var configuration = ReadJson(configurationPath);
                if (corruption == "requiredFlag") configuration["symbols"]![canonical]!["isRequired"] = false;
                else
                {
                    configuration["symbols"]![canonical]!["defaultValue"] = "js-queue-items";
                    mapping.AsObject().Remove("nativeDefaults");
                }
                mapping["configuration"]!["sha256"] = Write(configurationPath, configuration.ToJsonString());
            }
            else
            {
                if (corruption == "otherIdentity") defaults.AsObject().Remove(identity);
                else if (corruption == "originalDefault") defaults[identity]![canonical]!["sourceDefaultValue"] = "different-original";
                else if (corruption == "nativeDefault") defaults[identity]![canonical]!["defaultValue"] = "different-native";
                else if (corruption == "unknownParameter") defaults[identity]!["not-a-parameter"] = defaults[identity]![canonical]!.DeepClone();
                string text = defaults.ToJsonString();
                if (corruption == "duplicateParameter") text = text.Replace("\"sourceDefaultValue\":\"js-queue-items\"",
                    "\"sourceDefaultValue\":\"js-queue-items\",\"sourceDefaultValue\":\"js-queue-items\"", StringComparison.Ordinal);
                mapping["nativeDefaults"]!["sha256"] = Write(path, text);
            }
            Write(NodeItemCompatibility.ManifestPath, manifest.ToJsonString());

            JsonNode ReadJson(string member)
            {
                using var reader = new StreamReader(zip.GetEntry(member)!.Open());
                return JsonNode.Parse(reader.ReadToEnd())!;
            }

            string Write(string member, string text)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                zip.GetEntry(member)?.Delete();
                using var output = zip.CreateEntry(member).Open();
                output.Write(bytes);
                return Convert.ToHexString(SHA256.HashData(bytes));
            }
        }
        string hive = Path.Combine(_root, "default-hive");
        await TemplateFixtures.InstallAsync(archive, hive, CancellationToken.None);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        var bundle = new TemplateEngineBundleContext("Microsoft.Azure.Functions.ExtensionBundle", "4.35.0", _root);
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(_root),
            bundle: new(bundle.Id, bundle.Version, bundle.Path, new(bundle, ["serviceBusTrigger"])), enableBundleBindingConstraints: true);
        using var session = new TemplateEngineSession(context, hive, availability);
        using var templater = new Templater(session);
        var selected = (await templater.ResolveGroupAsync(identity, TemplateType.Item, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));

        RecordingNodeSourceAuthority authority = new();
        var result = await new NodeItemCompatibility(_interaction, sourceAuthority: authority)
            .ResolveAsync(selected, arguments, null, false, CancellationToken.None);

        result.Succeeded.Should().Be(succeeds);
        if (succeeds)
        {
            arguments.ValuesByCanonicalName[canonical].Should().Be("js-queue-items");
            authority.Calls.Should().BeGreaterThan(0);
        }
        else
        {
            result.Diagnostics.Should().ContainSingle().Which.Should().Contain("owned compatibility source");
            authority.Calls.Should().Be(0, "original and native default/source validation must reject before host proof");
        }
        Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
    }

    private static string? ExpectedNodeDefault(string identity, string canonical, string? original)
        => (identity, canonical) switch
        {
            ("EventHubTrigger-JavaScript", "eventHubTrigger-name") => "samples-workitems",
            ("ServiceBusQueueTrigger-JavaScript", "serviceBusTrigger-name") => "myinputqueue",
            ("ServiceBusTopicTrigger-JavaScript", "serviceBusTrigger-topic") => "mytopic",
            ("ServiceBusTopicTrigger-JavaScript", "serviceBusTrigger-subscription") => "mysubscription",
            ("EventHubTrigger-TypeScript", "eventHubTrigger-name") => "samples-workitems",
            ("ServiceBusQueueTrigger-TypeScript", "serviceBusTrigger-name") => "myinputqueue",
            ("ServiceBusTopicTrigger-TypeScript", "serviceBusTrigger-topic") => "mytopic",
            ("ServiceBusTopicTrigger-TypeScript", "serviceBusTrigger-subscription") => "mysubscription",
            _ => original,
        };

    private sealed class RecordingNodeSourceAuthority : NodeConvertedSourceAuthority
    {
        public int Calls { get; private set; }

        public override bool Matches(NodeItemSource source)
        {
            Calls++;
            return base.Matches(source);
        }
    }
}
