// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    private const string EventHubNamePattern =
        "^[a-zA-Z0-9]$|^[a-zA-Z0-9][a-zA-Z0-9-_.]{0,48}[a-zA-Z0-9]$|^[{][a-zA-Z0-9]{1,126}[}]$|^[%][a-zA-Z0-9]{1,126}[%]$";

    [Theory]
    [InlineData("stable", "bad/name", false)]
    [InlineData("preview", "bad/name", false)]
    [InlineData("experimental", "bad/name", false)]
    [InlineData("stable", "orders-1._x", true)]
    [InlineData("preview", "orders-1._x", true)]
    [InlineData("experimental", "orders-1._x", true)]
    [InlineData("stable", "{HubSetting}", true)]
    [InlineData("preview", "{HubSetting}", true)]
    [InlineData("experimental", "{HubSetting}", true)]
    [InlineData("stable", "%HubSetting%", true)]
    [InlineData("preview", "%HubSetting%", true)]
    [InlineData("experimental", "%HubSetting%", true)]
    public async Task PromptLookup_ActualEventHubArguments_PreserveOwnedRegexBeforeGenerationAndAppend(
        string channel, string eventHubName, bool expectedSuccess)
    {
        _channels.RootElement.EnumerateArray().Single(item => item.GetProperty("channel").GetString() == channel)
            .GetProperty("templateIds").EnumerateArray().Select(item => item.GetString()).Should().Contain("EventHubTrigger-Python");
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync(channel));
        var selected = (await templater.ResolveGroupAsync("EventHubTrigger-Python", TemplateType.Item)).Group!.Single();
        var provider = CreatePythonProvider();
        var parser = new TemplateArgumentParser();
        string[] tokens = ["--eventhub-trigger-eventhub-name", eventHubName];
        var arguments = parser.Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), tokens));
        arguments.IsComplete.Should().BeTrue("the native scalar parser accepts the string before original regex validation");
        arguments.ExplicitParameterNames.Should().Equal("eventhubTrigger-eventhubName");
        arguments.ValuesByCanonicalName["eventhubTrigger-eventhubName"].Should().Be(eventHubName);
        arguments.Parameters.Single(item => item.Definition.CanonicalName == "eventhubTrigger-eventhubName")
            .LongAlias.Should().Be("--eventhub-trigger-eventhub-name");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "prompt-lookup-existing"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        const string prefix = "import azure.functions as func\napp = func.FunctionApp()\n# user-owned prefix";
        File.WriteAllText(app, prefix);
        var compatibility = await provider.Compatibility.ResolveAsync(selected, arguments, "ValidFunction", false, CancellationToken.None);

        compatibility.Succeeded.Should().Be(expectedSuccess, "the original Event Hub name regex must reject bad/name without changing valid forms");
        compatibility.Name.Should().Be("ValidFunction");
        if (!expectedSuccess)
        {
            compatibility.Diagnostics.Should().ContainSingle().Which.Should()
                .Contain("eventhubTrigger-eventhubName").And.Contain(EventHubNamePattern);
        }

        var interaction = Substitute.For<IInteractionService>();
        var contexts = Substitute.For<INewExecutionContextResolver>();
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), false, Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(templater.Context, null));
        var factory = Substitute.For<ITemplaterFactory>();
        factory.CreateAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>()).Returns(templater);
        NewExecutionRunner runner = new(contexts, factory, parser, interaction, new(parser, interaction));
        NewExecutionRequest request = new(output, "EventHubTrigger-Python", "ValidFunction", "Python",
            false, true, false, false, false, tokens, new HashSet<string>())
        {
            ItemCompatibility = provider.Compatibility,
            SelectedItemEffects = provider.Effects,
        };

        var result = await runner.ExecuteAsync(request, new([]), CancellationToken.None);

        result.Succeeded.Should().Be(expectedSuccess, string.Join(" ", result.Diagnostics));
        if (expectedSuccess)
        {
            result.Invocation!.Status.Should().Be(TemplateInvocationStatus.Success, result.Invocation.Diagnostic);
            result.Invocation.Effects!.Kind.Should().Be("applied");
            File.ReadAllText(app).Should().StartWith(prefix + Environment.NewLine)
                .And.Contain("def ValidFunction(", $"event_hub_name=\"{eventHubName}\"");
        }
        else
        {
            result.Kind.Should().Be("invalidLegacyInput");
            result.Invocation.Should().BeNull("compatibility refusal precedes native creation and append execution");
            File.ReadAllText(app).Should().Be(prefix);
        }
        Directory.GetFiles(output.Info.FullName).Select(Path.GetFileName).Should().Equal("function_app.py");
    }

    [Theory]
    [InlineData("CosmosDbTrigger-Python", "cosmosDbTrigger-connection", "cosmosDBTrigger-connection",
        "Cosmos DB account connection", "CosmosDbConnectionString")]
    [InlineData("CosmosDbTrigger-Python", "cosmosDbTrigger-databaseName", "cosmosDBTrigger-databaseName", "Database name", "database_name")]
    [InlineData("CosmosDbTrigger-Python", "cosmosDbTrigger-containerName", "cosmosDBTrigger-containerName", "Container name", "container_name")]
    [InlineData("EventHubTrigger-Python", "eventhubTrigger-eventhubName", "eventHubTrigger-eventHubName", "Event Hub name", "eventhubname")]
    [InlineData("EventHubTrigger-Python", "eventhubTrigger-connection", "eventHubTrigger-connection", "Event Hub connection", "EventHubConnectionString")]
    public async Task PromptLookup_ActualCaseVariantDocuments_PreserveMetadataAndRawCanonicalIds(
        string id, string canonicalName, string ownedPromptId, string description, string defaultValue)
    {
        await InstallAsync();
        foreach (var channel in _channels.RootElement.EnumerateArray().Where(item =>
            item.GetProperty("templateIds").EnumerateArray().Any(template => template.GetString() == id)))
        {
            using var templater = CreateTemplater(await ReadBundleAsync(channel.GetProperty("channel").GetString()!));
            var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
            var source = await new PythonPolicyTestReader().ReadAsync(selected, CancellationToken.None);
            var rule = source.Compatibility!.Rules.Single(item => item.CanonicalName == canonicalName);
            using var prompts = JsonDocument.Parse(await selected.ReadPackageFileAsync("source/userPrompts.json", CancellationToken.None));
            var owned = prompts.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetString() == ownedPromptId);
            var rawInput = _sources.Single(item => item.Id == id).Jobs!.Single(job => job.Type == "CreateNewApp")
                .Inputs!.Single(input => input.ParamId == canonicalName);
            using var resources = JsonDocument.Parse(await selected.ReadPackageFileAsync("source/resources.json", CancellationToken.None));
            var label = owned.GetProperty("label").GetString()!;
            resources.RootElement.GetProperty(label[1..]).GetString().Should().Be(description);
            ownedPromptId.Should().NotBe(canonicalName).And.BeEquivalentTo(canonicalName);
            rule.Prompt.Id.Should().Be(canonicalName);
            rule.Prompt.Description.Should().Be(description);
            rule.Prompt.DefaultValue.Should().Be(defaultValue).And.Be(rawInput.DefaultValue);
            rule.Prompt.IsRequired.Should().BeTrue();
            rule.Prompt.DataType.Should().Be("string");
            rule.Prompt.Choices.Should().BeNullOrEmpty();
            selected.Parameters.Should().Contain(parameter => parameter.CanonicalName == canonicalName);
            var expectedPattern = owned.TryGetProperty("validators", out var validators)
                ? validators[0].GetProperty("expression").GetString() : null;
            rule.Prompt.ValidatorRegex.Should().Be(expectedPattern);
            if (canonicalName == "eventhubTrigger-eventhubName")
            {
                expectedPattern.Should().Be(EventHubNamePattern);
            }
            var map = await selected.ReadPackageFileAsync(source.Compatibility.Source.Identity, CancellationToken.None);
            source.Compatibility.Source.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(map.Span)));
            source.Compatibility.Package.Should().Be(selected.Entry.Package);
            Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromptLookup_SelfConsistentCaseCollidingPromptIds_RefuseExplicitlyBeforeCompatibilityOrEffects(bool collision)
    {
        if (collision)
        {
            using var archive = ZipFile.Open(_archive, ZipArchiveMode.Update);
            JsonNode prompts;
            JsonNode map;
            using (StreamReader reader = new(archive.GetEntry("source/userPrompts.json")!.Open()))
            {
                prompts = JsonNode.Parse(reader.ReadToEnd())!;
            }
            using (StreamReader reader = new(archive.GetEntry("source/applications.json")!.Open()))
            {
                map = JsonNode.Parse(reader.ReadToEnd())!;
            }
            var duplicate = prompts.AsArray().Single(item => item!["id"]!.GetValue<string>() == "eventHubTrigger-eventHubName")!.DeepClone();
            duplicate["id"] = "EVENTHUBTRIGGER-EVENTHUBNAME";
            prompts.AsArray().Add(duplicate);
            var bytes = Encoding.UTF8.GetBytes(prompts.ToJsonString());
            map["sources"]!.AsArray().Single(item => item!["path"]!.GetValue<string>() == "source/userPrompts.json")!["sha256"]
                = Convert.ToHexStringLower(SHA256.HashData(bytes));
            archive.GetEntry("source/userPrompts.json")!.Delete();
            using (var stream = archive.CreateEntry("source/userPrompts.json").Open())
            {
                stream.Write(bytes);
            }
            archive.GetEntry("source/applications.json")!.Delete();
            using StreamWriter writer = new(archive.CreateEntry("source/applications.json").Open());
            writer.Write(map.ToJsonString());
        }
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("EventHubTrigger-Python", TemplateType.Item)).Group!.Single();
        var readerSource = new PythonPolicyTestReader();
        var provider = CreatePythonProvider();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--eventhub-trigger-eventhub-name", "orders-1._x"]));
        using var currentMap = JsonDocument.Parse(await selected.ReadPackageFileAsync("source/applications.json", CancellationToken.None));
        var descriptor = currentMap.RootElement.GetProperty("sources").EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == "source/userPrompts.json");
        var actualPrompts = await selected.ReadPackageFileAsync("source/userPrompts.json", CancellationToken.None);
        Convert.ToHexStringLower(SHA256.HashData(actualPrompts.Span)).Should().Be(descriptor.GetProperty("sha256").GetString());
        if (collision)
        {
            Func<Task> read = () => readerSource.ReadAsync(selected, CancellationToken.None);
            await read.Should().ThrowAsync<InvalidDataException>().WithMessage("Python prompt identities are missing or duplicated.");
        }
        else
        {
            (await readerSource.ReadAsync(selected, CancellationToken.None)).Binding.Diagnostics.Should().BeEmpty();
        }

        var compatibility = await provider.Compatibility.ResolveAsync(selected, arguments, "ValidFunction", false, CancellationToken.None);
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);

        compatibility.Succeeded.Should().Be(!collision);
        if (collision)
        {
            compatibility.Diagnostics.Should().ContainSingle().Which.Should().Contain("Python prompt identities are missing or duplicated.");
            binding.Diagnostics.Should().ContainSingle().Which.Should().Contain("Python prompt identities are missing or duplicated.");
            binding.Append.Should().BeNull();
        }
        else
        {
            binding.Diagnostics.Should().BeEmpty();
            binding.Append.Should().NotBeNull();
        }
        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
    }
}
