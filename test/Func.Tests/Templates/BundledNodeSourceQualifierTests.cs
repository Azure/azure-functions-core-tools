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
        Directory.CreateDirectory(Path.Combine(metadataRoot, "bin"));
        File.WriteAllText(Path.Combine(metadataRoot, "bin", "extensions.json"), NodeBridgeExtensions);
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

            source.Sources.Should().HaveCount(8);
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

    private async Task<NodeItemSource> ReadQualifiedSourceAsync()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None, availability);
        using var templater = new Templater(session);
        var selected = (await templater.ResolveGroupAsync("TimerTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
        return await NodeItemCompatibility.ReadSourceAsync(selected, CancellationToken.None);
    }
}
