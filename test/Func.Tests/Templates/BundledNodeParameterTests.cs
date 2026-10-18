// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.V2;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    private static string ExpectedOperativeNodeSource(NewTemplate template, string name, IReadOnlyDictionary<string, string?> values)
    {
        string expected = template.Files!.Single().Value.Replace("$(FUNCTION_NAME_INPUT)", name, StringComparison.Ordinal);
        if (template.Id == "DaprPublishOutputBinding-JavaScript")
        {
            expected = expected.Replace("app.generic('DaprTopicTriggerNodeV4', {", $"app.generic('{name}_Topic', {{", StringComparison.Ordinal);
        }
        else if (template.Id == "DaprServiceInvocationTrigger-JavaScript")
        {
            expected = expected.Replace("app.http('InvokeOutputBinding', {", $"app.http('{name}_Invoke', {{", StringComparison.Ordinal)
                .Replace("route: \"invoke/{appId}/{methodName}\"", $"route: \"{name}/invoke/{{appId}}/{{methodName}}\"", StringComparison.Ordinal)
                .Replace("http://localhost:7071/api/invoke/functionapp/{yourFunctionName}", $"http://localhost:7071/api/{name}/invoke/functionapp/{name}", StringComparison.Ordinal)
                .Replace("--method {yourFunctionName}", $"--method {name}", StringComparison.Ordinal);
        }
        foreach (var input in template.Jobs![0].Inputs!.Where(input => input.ParamId != "trigger-functionName"))
        {
            string property = input.ParamId switch
            {
                "timerTrigger-schedule" => "schedule",
                "queueTrigger-connection" or "blobTrigger-connection" or "serviceBusTrigger-connection" => "connection",
                "queueTrigger-name" or "serviceBusTrigger-name" => "queueName",
                "blobTrigger-path" => "path",
                "eventHubTrigger-name" => "eventHubName",
                "serviceBusTrigger-topic" => "topicName",
                "serviceBusTrigger-subscription" => "subscriptionName",
                "cosmosTrigger-connection" => "connectionStringSetting",
                "cosmosTrigger-database" => "databaseName",
                "cosmosTrigger-collection" => "collectionName",
                "cosmosTrigger-createLease" => "createLeaseCollectionIfNotExists",
                _ => throw new InvalidOperationException("The test requires an independently reviewed expected property."),
            };
            string value = values[input.ParamId!]!;
            string literal = input.ParamId == "cosmosTrigger-createLease" ? value : "'" + value + "'";
            expected = System.Text.RegularExpressions.Regex.Replace(expected, $"(?m)^    {property}: [^\\r\\n]*,",
                _ => $"    {property}: {literal},");
        }
        return expected;
    }

    [Theory]
    [InlineData("JavaScript", ".js")]
    [InlineData("TypeScript", ".ts")]
    public async Task Parameters_ActualSelectedTimer_PreservesNonDefaultScheduleAndSourceProof(string language, string extension)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Directory.CreateDirectory(Path.Combine(_root, "timer-output")).FullName;
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(target),
            new(WorkingDirectory.FromExplicit(_root), "node", language));
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0"), ["node"], true)]);
        using var session = await _templates.OpenReadAsync(context, CancellationToken.None, availability);
        using var templater = new Templater(session);
        var selected = (await templater.ResolveGroupAsync("TimerTrigger-" + language, TemplateType.Item, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(),
            ["--timer-trigger-schedule", "0 */7 * * * *"]), CancellationToken.None);
        arguments.IsComplete.Should().BeTrue();
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(target), arguments.ValuesByCanonicalName, "QuotedCron");

        var source = await Azure.Functions.Cli.Templates.Node.NodeItemCompatibility.ReadSourceAsync(selected, CancellationToken.None);
        var preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);
        Directory.GetFiles(target).Should().BeEmpty();
        var created = await selected.InvokeAsync(request, CancellationToken.None);

        source.Template.Files!.Single().Value.Should().Contain("schedule: '0 */5 * * * *'");
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Changes.Should().BeEquivalentTo(preview.Changes);
        File.ReadAllText(Path.Combine(target, "QuotedCron" + extension)).Should().Contain("schedule: '0 */7 * * * *'");
    }

    [Theory]
    [InlineData("JavaScript", ".js")]
    [InlineData("TypeScript", ".ts")]
    public async Task Parameters_ActualNativeQueue_UsesEachAssociatedProperty(string language, string extension)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var native = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single(item => item.Identity == "QueueTrigger-" + language);
        var creator = new TemplateCreator(session.Settings);
        string target = Path.Combine(_root, "queue-output");
        var values = new Dictionary<string, string?>
        {
            ["queueTrigger-connection"] = "LaneHStorage",
            ["queueTrigger-name"] = "lane-h-jobs",
        };

        var created = await creator.InstantiateAsync(native, "ActualQueue", "ActualQueue", target, values, cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        File.ReadAllText(Path.Combine(target, "ActualQueue" + extension)).Should()
            .Contain("connection: 'LaneHStorage'", "queueName: 'lane-h-jobs'")
            .And.NotContain("queueName: 'js-queue-items'");
    }

    [Theory]
    [InlineData("BlobTrigger", "blobTrigger-connection", "connection: 'HBlobStorage'")]
    [InlineData("BlobTrigger", "blobTrigger-path", "path: 'h-input/{name}'")]
    [InlineData("EventGridBlobTrigger", "blobTrigger-connection", "connection: 'HBlobStorage'")]
    [InlineData("EventGridBlobTrigger", "blobTrigger-path", "path: 'h-input/{name}'")]
    [InlineData("EventHubTrigger", "eventHubTrigger-name", "eventHubName: 'h-events'")]
    [InlineData("ServiceBusQueueTrigger", "serviceBusTrigger-connection", "connection: 'HServiceBus'")]
    [InlineData("ServiceBusQueueTrigger", "serviceBusTrigger-name", "queueName: 'h-work'")]
    [InlineData("ServiceBusTopicTrigger", "serviceBusTrigger-connection", "connection: 'HServiceBus'")]
    [InlineData("ServiceBusTopicTrigger", "serviceBusTrigger-topic", "topicName: 'h-topic'")]
    [InlineData("ServiceBusTopicTrigger", "serviceBusTrigger-subscription", "subscriptionName: 'h-subscription'")]
    [InlineData("CosmosDBTrigger", "cosmosTrigger-connection", "connectionStringSetting: 'HCosmos'")]
    [InlineData("CosmosDBTrigger", "cosmosTrigger-database", "databaseName: 'h-database'")]
    [InlineData("CosmosDBTrigger", "cosmosTrigger-collection", "collectionName: 'h-collection'")]
    [InlineData("CosmosDBTrigger", "cosmosTrigger-createLease", "createLeaseCollectionIfNotExists: false")]
    public async Task Parameters_ReviewedRelatedPrompts_TargetTheirOwnPropertiesInBothLanguages(string kind, string prompt, string declaration)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var natives = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        var creator = new TemplateCreator(session.Settings);
        string value = declaration.Split(": ", 2)[1].Trim('\'');
        foreach (string language in new[] { "JavaScript", "TypeScript" })
        {
            var native = natives.Single(item => item.Identity == kind + "-" + language);
            string target = Path.Combine(_root, kind, language);
            var raw = ReadNodeSource().Templates.Single(item => item.Id == native.Identity);
            var values = raw.Jobs![0].Inputs!.Where(input => input.ParamId != "trigger-functionName")
                .ToDictionary(input => input.ParamId!, input => input.DefaultValue);
            values[prompt] = value;

            var created = await creator.InstantiateAsync(native, "RelatedProperty", "RelatedProperty", target, values,
                cancellationToken: CancellationToken.None);

            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            string extension = language == "JavaScript" ? ".js" : ".ts";
            File.ReadAllText(Path.Combine(target, "RelatedProperty" + extension)).Should().Contain(declaration);
        }
    }

    [Theory]
    [InlineData("JavaScript", ".js")]
    [InlineData("TypeScript", ".ts")]
    public async Task Parameters_StringEncoding_PreservesQuotesBackslashesAndControlCharacters(string language, string extension)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var native = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single(item => item.Identity == "TimerTrigger-" + language);
        var creator = new TemplateCreator(session.Settings);
        string target = Path.Combine(_root, "escaped-output");
        var values = new Dictionary<string, string?> { ["timerTrigger-schedule"] = "'\"\\\r\n\t\u2028\u2029" };

        var created = await creator.InstantiateAsync(native, "EscapedTimer", "EscapedTimer", target, values,
            cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        File.ReadAllText(Path.Combine(target, "EscapedTimer" + extension)).Should()
            .Contain("schedule: '\\u0027\\\"\\\\\\r\\n\\t\\u2028\\u2029',")
            .And.Contain("context.log('Timer function processed request.');");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("True")]
    public async Task Parameters_BooleanChoice_RendersAJavaScriptBooleanRatherThanAStringOrIdentifier(string value)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var natives = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        foreach (string language in new[] { "JavaScript", "TypeScript" })
        {
            var native = natives.Single(item => item.Identity == "CosmosDBTrigger-" + language);
            var values = new Dictionary<string, string?>
            {
                ["cosmosTrigger-connection"] = "HCosmos",
                ["cosmosTrigger-database"] = "h-database",
                ["cosmosTrigger-collection"] = "h-collection",
                ["cosmosTrigger-createLease"] = value,
            };
            string target = Path.Combine(_root, language);
            var creator = new TemplateCreator(session.Settings);

            var created = await creator.InstantiateAsync(native, "BooleanValue", "BooleanValue", target, values,
                cancellationToken: CancellationToken.None);

            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            string extension = language == "JavaScript" ? ".js" : ".ts";
            File.ReadAllText(Path.Combine(target, "BooleanValue" + extension)).Should()
                .Contain("createLeaseCollectionIfNotExists: " + (value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "false," : "true,"));
        }
    }
}
