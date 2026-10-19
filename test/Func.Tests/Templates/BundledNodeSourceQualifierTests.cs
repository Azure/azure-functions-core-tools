// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Templates.V2;
using Azure.Functions.Cli.Workloads.Node;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public async Task SourceQualifier_AllActualOwnedItems_PinsOriginalAndNativeSourcesWithoutChoosingOutputDirectories()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string metadataRoot = Path.Combine(_root, "layout-bundle");
        Directory.CreateDirectory(Path.Combine(metadataRoot, "4.35.0", "bin"));
        File.WriteAllText(Path.Combine(metadataRoot, "4.35.0", "bin", "extensions.json"), NodeBridgeExtensions);
        var bundle = await new TemplateEngineBundleContextReader(_files).ReadAsync(
            new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.35.0", metadataRoot, null), CancellationToken.None);
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(_root), bundle: bundle, enableBundleBindingConstraints: true);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var session = await _templates.OpenReadAsync(context, CancellationToken.None, availability);
        using var templater = new Templater(session);
        foreach (var raw in ReadNodeSource().Templates)
        {
            var selected = (await templater.ResolveGroupAsync(raw.Id!, TemplateType.Item, CancellationToken.None)).Group!.Single();

            var source = await NodeItemCompatibility.ReadSourceAsync(selected, CancellationToken.None);

            source.Sources.Should().HaveCount(raw.Id is "EventHubTrigger-JavaScript" or "ServiceBusQueueTrigger-JavaScript"
                or "ServiceBusTopicTrigger-JavaScript" ? 9 : 8);
            foreach (var pin in source.Sources)
            {
                pin.TemplateIdentity.Should().Be(selected.Entry.Identity);
                var bytes = await selected.ReadPackageFileAsync(pin.Identity, CancellationToken.None);
                pin.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes.Span)));
            }
            source.Sources.Should().Contain(source.ContentSource);
        }
        Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
    }

    [Theory]
    [InlineData("../outside/$(FUNCTION_NAME_INPUT).js")]
    [InlineData("/absolute/$(FUNCTION_NAME_INPUT).js")]
    [InlineData("C:/absolute/$(FUNCTION_NAME_INPUT).js")]
    [InlineData("C:relative/$(FUNCTION_NAME_INPUT).js")]
    [InlineData("//server/share/$(FUNCTION_NAME_INPUT).js")]
    [InlineData("src\\functions\\$(FUNCTION_NAME_INPUT).js")]
    [InlineData("src/$(PATH_INPUT)/$(FUNCTION_NAME_INPUT).js")]
    [InlineData("src/functions/$(FUNCTION_NAME_INPUT).ts")]
    [InlineData("src/functions/fixed.js")]
    [InlineData("src/./functions/$(FUNCTION_NAME_INPUT).js")]
    [InlineData("src//functions/$(FUNCTION_NAME_INPUT).js")]
    public async Task SourceQualifier_UnqualifiedOriginalPath_RefusesInsteadOfGuessing(string path)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync();
        source.Template.Actions!.Single(action => action.Type == "WriteToFile").FilePath = path;

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().Throw<InvalidDataException>();
        Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
    }

    [Theory]
    [InlineData("secondWrite")]
    [InlineData("duplicateAction")]
    [InlineData("wrongJob")]
    [InlineData("unmatchedBody")]
    [InlineData("changedBody")]
    [InlineData("nativeBody")]
    [InlineData("nativeName")]
    [InlineData("foreignContentPath")]
    [InlineData("nativeFileRename")]
    [InlineData("nativeNameDirectory")]
    [InlineData("userIntentParameter")]
    [InlineData("foreignIntentBinding")]
    public async Task SourceQualifier_ChangedActionOrBodyAssociation_Refuses(string corruption)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync();
        var raw = source.Template;
        if (corruption == "secondWrite") raw.Actions!.Add(new V2Action { Name = "extra", Type = "WriteToFile", FilePath = "extra.js" });
        else if (corruption == "duplicateAction") raw.Jobs![0].Actions![1] = raw.Jobs[0].Actions![0];
        else if (corruption == "wrongJob") raw.Jobs![0].Type = "AppendToFile";
        else if (corruption == "unmatchedBody") raw.Actions![1].Source = "$(OTHER_CONTENT)";
        else if (corruption == "changedBody") raw.Files!["function.js"] += "\nchanged";
        else if (corruption == "nativeBody") source = source with { Content = Encoding.UTF8.GetBytes("different native source") };
        else if (corruption == "foreignContentPath") source = source with { ContentPath = "content/foreign/funcNodeItem.js" };
        else
        {
            var config = JsonNode.Parse(source.Configuration.Span)!;
            if (corruption == "nativeNameDirectory") config["preferNameDirectory"] = true;
            else if (corruption == "nativeFileRename") config["symbols"]!["timerTrigger-schedule"]!["fileRename"] = "funcNodeItem";
            else if (corruption == "userIntentParameter") config["symbols"]!["InvocationOutputIntent"]!["type"] = "parameter";
            else if (corruption == "foreignIntentBinding") config["symbols"]!["InvocationOutputIntent"]!["binding"] = "host:WorkingDirectory";
            else config["sourceName"] = "OtherNativeName";
            source = source with { Configuration = Encoding.UTF8.GetBytes(config.ToJsonString()) };
        }

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task SourceQualifier_Arguments_RejectInvalidSourceAndLanguage()
    {
        Action missing = () => NodeItemSourceQualifier.Validate(null!, "JavaScript");
        missing.Should().Throw<ArgumentNullException>();
        NodeItemSource source = await ReadQualifiedSourceAsync();
        Action invalidLanguage = () => NodeItemSourceQualifier.Validate(source, "js");
        invalidLanguage.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("transform")]
    [InlineData("valueSource")]
    [InlineData("replaceToken")]
    [InlineData("unescapedParameter")]
    [InlineData("stringForm")]
    [InlineData("assignTo")]
    [InlineData("missingDeclaration")]
    [InlineData("duplicateDeclaration")]
    [InlineData("extraReplacement")]
    public async Task SourceQualifier_ChangedParameterAssociation_RefusesInsteadOfTrustingCanonicalization(string corruption)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync();
        var config = JsonNode.Parse(source.Configuration.Span)!;
        var derived = config["symbols"]!["timerTrigger-schedule-NodeString"]!;
        if (corruption == "transform") derived["valueTransform"] = "identity";
        else if (corruption == "valueSource") derived["valueSource"] = "name";
        else if (corruption == "replaceToken") derived["replaces"] = "Timer";
        else if (corruption == "unescapedParameter") config["symbols"]!["timerTrigger-schedule"]!["replaces"] = "__FUNC_NODE_timerTrigger-schedule__";
        else if (corruption == "stringForm") config["forms"]!["nodeApostrophe"]!["replacement"] = "";
        else if (corruption == "extraReplacement") config["symbols"]!["Unrelated"] =
            JsonNode.Parse("""{"type":"parameter","datatype":"string","replaces":"Timer"}""");
        else if (corruption == "assignTo") source.Template.Jobs![0].Inputs![1].AssignTo = "$(UNRELATED_INPUT)";
        else if (corruption == "missingDeclaration") source.Template.Files!["function.js"] =
            source.Template.Files["function.js"].Replace("schedule: ", "differentProperty: ", StringComparison.Ordinal);
        else source.Template.Files!["function.js"] += "\n    schedule: '0 */5 * * * *',\n";
        source = source with { Configuration = Encoding.UTF8.GetBytes(config.ToJsonString()) };

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task SourceQualifier_UnreviewedBodyComments_RequireAssociationReviewEvenWhenNativeBytesMatch()
    {
        NodeItemSource source = await ReadQualifiedSourceAsync();
        const string decoys = "\n// schedule: '0 */5 * * * *',\nconst unrelated = '0 */5 * * * *';\n";
        source.Template.Files!["function.js"] += decoys;
        source = source with { Content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source.Content.Span) + decoys) };

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().Throw<InvalidDataException>().WithMessage("*complete body/declaration association*");
        source.Content.Span.ToArray().Should().EndWith(Encoding.UTF8.GetBytes(decoys));
    }

    [Fact]
    public async Task SourceQualifier_ExactHistoricalParameterEncoding_PreservesQualifiedRawBody()
    {
        NodeItemSource source = HistoricalSource(await ReadQualifiedSourceAsync());

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().NotThrow();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceQualifier_ReviewedBodyLineEndings_AllowOnlyTheExistingCrLfLfRepresentationDifference(bool historical)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync();
        if (historical) source = HistoricalSource(source);
        source.Template.Files!["function.js"] = source.Template.Files["function.js"].Replace("\r\n", "\n", StringComparison.Ordinal);
        source = source with { Content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source.Content.Span)
            .Replace("\r\n", "\n", StringComparison.Ordinal)) };

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().NotThrow();
        source.Template.Files["function.js"] += " ";
        qualify.Should().Throw<InvalidDataException>().WithMessage("*complete body/declaration association*");
    }

    [Theory]
    [InlineData("TimerTrigger-JavaScript", "timerTrigger-schedule", "0 */5 * * * *", true)]
    [InlineData("TimerTrigger-JavaScript", "timerTrigger-schedule", "0 */7 * * * *", false)]
    [InlineData("TimerTrigger-JavaScript", "timerTrigger-schedule", null, false)]
    [InlineData("QueueTrigger-JavaScript", "queueTrigger-name", "js-queue-items", true)]
    [InlineData("QueueTrigger-JavaScript", "queueTrigger-name", "custom-queue", false)]
    [InlineData("QueueTrigger-JavaScript", "queueTrigger-connection", "RequestedStorage", false)]
    [InlineData("EventHubTrigger-JavaScript", "eventHubTrigger-name", "", false)]
    [InlineData("EventHubTrigger-JavaScript", "eventHubTrigger-name", "samples-workitems", true)]
    [InlineData("ServiceBusQueueTrigger-JavaScript", "serviceBusTrigger-name", "js-queue-items", false)]
    [InlineData("ServiceBusQueueTrigger-JavaScript", "serviceBusTrigger-name", "myinputqueue", true)]
    [InlineData("ServiceBusTopicTrigger-JavaScript", "serviceBusTrigger-topic", "mytopic", false)]
    [InlineData("ServiceBusTopicTrigger-JavaScript", "serviceBusTrigger-subscription", "mysubscription", false)]
    [InlineData("CosmosDBTrigger-JavaScript", "cosmosTrigger-createLease", "TRUE", true)]
    [InlineData("CosmosDBTrigger-JavaScript", "cosmosTrigger-createLease", "false", false)]
    public async Task SourceQualifier_HistoricalArguments_RequireEveryEffectiveValueToMatchItsOwnedLiteral(
        string identity, string parameter, string? value, bool succeeds)
    {
        NodeItemSource source = HistoricalSource(await ReadQualifiedSourceAsync(identity));
        NodeItemSourceQualifier.Validate(source, "JavaScript");
        var values = source.Template.Jobs![0].Inputs!.Where(input => input.ParamId != "trigger-functionName")
            .ToDictionary(input => input.ParamId!, input => input.DefaultValue);
        values[parameter] = value;
        const string hint = "Run 'func workload update Physical.Selected.Node --version 1.2.3 --exact'.";

        Action validate = () => NodeItemSourceQualifier.ValidateArguments(source, values);

        if (succeeds)
        {
            validate.Should().NotThrow();
        }
        else
        {
            var failure = validate.Should().Throw<NodeItemParameterCompatibilityException>().WithMessage("*cannot apply parameter*").Which;
            failure.Message.Should().Contain(identity);
            var compatibility = new NodeItemCompatibility(_interaction, () => hint);
            compatibility.DescribeUnsupportedParameters(failure).Should().Contain(hint).And.Contain("Nothing was generated");
        }
    }

    [Theory]
    [InlineData("formsNull")]
    [InlineData("mixedDerived")]
    [InlineData("parameterReplacement")]
    [InlineData("parameterForm")]
    public async Task SourceQualifier_HistoricalEncoding_DoesNotAdmitMixedOrUnqualifiedSubstitutionShapes(string corruption)
    {
        NodeItemSource source = HistoricalSource(await ReadQualifiedSourceAsync());
        var config = JsonNode.Parse(source.Configuration.Span)!;
        if (corruption == "formsNull") config["forms"] = null;
        else if (corruption == "mixedDerived") config["symbols"]!["unexpected-NodeString"] =
            JsonNode.Parse("""{"type":"derived","valueSource":"name","valueTransform":"identity","replaces":"Timer"}""");
        else if (corruption == "parameterReplacement") config["symbols"]!["timerTrigger-schedule"]!["replaces"] = "Timer";
        else config["symbols"]!["timerTrigger-schedule"]!["forms"] = JsonNode.Parse("""{"global":["lowerCase"]}""");
        source = source with { Configuration = Encoding.UTF8.GetBytes(config.ToJsonString()) };

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task SourceQualifier_HistoricalArgumentContracts_RejectMissingArgumentsAndPreserveCurrentOperativeInputs()
    {
        NodeItemSource current = await ReadQualifiedSourceAsync();
        NodeItemSource historical = HistoricalSource(current);
        Action nullSource = () => NodeItemSourceQualifier.ValidateArguments(null!, new Dictionary<string, string?>());
        Action nullParameters = () => NodeItemSourceQualifier.ValidateArguments(historical, null!);
        Action missing = () => NodeItemSourceQualifier.ValidateArguments(historical, new Dictionary<string, string?>());
        Action operative = () => NodeItemSourceQualifier.ValidateArguments(current,
            new Dictionary<string, string?> { ["timerTrigger-schedule"] = "0 */7 * * * *" });
        var compatibility = new NodeItemCompatibility(_interaction);
        Action nullDiagnostic = () => compatibility.DescribeUnsupportedParameters(null!);

        nullSource.Should().Throw<ArgumentNullException>();
        nullParameters.Should().Throw<ArgumentNullException>();
        missing.Should().Throw<NodeItemParameterCompatibilityException>().WithMessage("*cannot apply parameter*");
        operative.Should().NotThrow();
        nullDiagnostic.Should().Throw<ArgumentNullException>();
    }

    private static NodeItemSource HistoricalSource(NodeItemSource source)
    {
        var config = JsonNode.Parse(source.Configuration.Span)!;
        config.AsObject().Remove("forms");
        foreach (string name in config["symbols"]!.AsObject().Where(symbol => symbol.Value!["type"]!.GetValue<string>() == "derived")
            .Select(symbol => symbol.Key).ToArray())
        {
            config["symbols"]!.AsObject().Remove(name);
        }
        string original = source.Template.Files!.Single().Value.Replace("$(FUNCTION_NAME_INPUT)",
            config["sourceName"]!.GetValue<string>(), StringComparison.Ordinal);
        return source with { Configuration = Encoding.UTF8.GetBytes(config.ToJsonString()), Content = Encoding.UTF8.GetBytes(original) };
    }

    [Theory]
    [InlineData("queueBlockComment")]
    [InlineData("timerTemplateLiteral")]
    [InlineData("queueUnrelatedObject")]
    public async Task SourceQualifier_ValidJavaScriptDecoy_CannotAuthorizeAnUnrelatedOperativeProperty(string decoy)
    {
        string kind = decoy.StartsWith("queue", StringComparison.Ordinal) ? "QueueTrigger" : "TimerTrigger";
        string identity = kind + "-JavaScript";
        NodeItemSource source = await ReadQualifiedSourceAsync(identity);
        string raw = source.Template.Files!["function.js"];
        string native = Encoding.UTF8.GetString(source.Content.Span);
        string suffix;
        string nativeSuffix;
        if (kind == "QueueTrigger")
        {
            suffix = decoy == "queueBlockComment" ? "\n/*\n    connection: '',\n*/\n"
                : "\nconst decoy = {\n    connection: '',\n};\n";
            nativeSuffix = suffix.Replace("connection: ''", "connection: '__FUNC_NODE_queueTrigger-connection__'", StringComparison.Ordinal);
            raw = raw.Replace("connection: ''", "connection: 'changed'", StringComparison.Ordinal);
            native = native.Replace("connection: '__FUNC_NODE_queueTrigger-connection__'", "connection: 'changed'", StringComparison.Ordinal);
        }
        else
        {
            suffix = "\nconst decoy = `\n    schedule: '0 */5 * * * *',\n`;\n";
            nativeSuffix = suffix.Replace("0 */5 * * * *", "__FUNC_NODE_timerTrigger-schedule__", StringComparison.Ordinal);
            raw = raw.Replace("schedule: '0 */5 * * * *'", "schedule: '0 */9 * * * *'", StringComparison.Ordinal);
            native = native.Replace("schedule: '__FUNC_NODE_timerTrigger-schedule__'", "schedule: '0 */9 * * * *'", StringComparison.Ordinal);
        }
        source.Template.Files["function.js"] = raw + suffix;
        source = source with { Content = Encoding.UTF8.GetBytes(native + nativeSuffix) };
        string valid = Path.Combine(_root, "valid-native-decoy.js");
        File.WriteAllText(valid, Encoding.UTF8.GetString(source.Content.Span)
            .Replace("__FUNC_NODE_queueTrigger-name__", "requested-queue", StringComparison.Ordinal));
        (await EvaluateNodePropertyAsync(valid)).Should().Be(kind == "QueueTrigger" ? "\"changed\"" : "\"0 */9 * * * *\"");

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceQualifier_ProviderBothModes_EmitsOnlyGenericSourcePinsWithoutChangingRequestedTarget(bool legacy)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var commandDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", "literal-nested"));
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var session = await _templates.OpenReadAsync(new(commandDirectory), CancellationToken.None, availability);
        using var templater = new Templater(session);
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var command = NodeComposedItem(commandDirectory.Info.FullName, "HttpTrigger-JavaScript") with
        {
            OutputIntent = legacy ? NativeItemOutputIntent.LegacyWorkflowRelative : NativeItemOutputIntent.LiteralOutput,
        };
        var invocation = new TemplateInvocationRequest(commandDirectory, arguments.ValuesByCanonicalName, "QualifiedFunction");
        var provider = new NodeTemplateWorkflowProvider(_interaction, _files, new NodeNpmInstaller());

        var prepared = await provider.PrepareAsync(new(templater, selected, arguments, command, invocation), CancellationToken.None);

        prepared.Request!.OutputDirectory.Should().BeSameAs(invocation.OutputDirectory);
        prepared.Request.Name.Should().Be(invocation.Name);
        prepared.Request.Parameters.Should().BeSameAs(invocation.Parameters);
        prepared.Diagnostics.Should().BeEmpty();
        prepared.Request.SourceSnapshot.Should().NotBeNull();
        Directory.Exists(commandDirectory.Info.FullName).Should().BeFalse();
    }

    private async Task<NodeItemSource> ReadQualifiedSourceAsync(string identity = "TimerTrigger-JavaScript")
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        string metadataRoot = Path.Combine(_root, "qualification-bundle");
        Directory.CreateDirectory(Path.Combine(metadataRoot, "4.35.0", "bin"));
        File.WriteAllText(Path.Combine(metadataRoot, "4.35.0", "bin", "extensions.json"), NodeBridgeExtensions);
        var bundle = await new TemplateEngineBundleContextReader(_files).ReadAsync(
            new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.35.0", metadataRoot, null), CancellationToken.None);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root), bundle: bundle,
            enableBundleBindingConstraints: true), CancellationToken.None, availability);
        using var templater = new Templater(session);
        var selected = (await templater.ResolveGroupAsync(identity, TemplateType.Item, CancellationToken.None)).Group!.Single();
        return await NodeItemCompatibility.ReadSourceAsync(selected, CancellationToken.None);
    }
}
