// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads.Node.Native;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public async Task SourceQualifier_AllActualOwnedItems_PinsOriginalAndNativeSourcesWithoutChoosingOutputDirectories()
    {
        await InstallNodeAuthoringCatalogAsync();
        string metadataRoot = Path.Combine(_root, "layout-bundle");
        Directory.CreateDirectory(Path.Combine(metadataRoot, "bin"));
        File.WriteAllText(Path.Combine(metadataRoot, "bin", "extensions.json"), NodeBridgeExtensions);
        var bundle = await new TemplateEngineBundleContextReader(_files).ReadAsync(
            new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.35.0", metadataRoot, null), CancellationToken.None);
        var context = new TemplateEngineContext(WorkingDirectory.FromExplicit(_root), bundle: bundle, enableBundleBindingConstraints: true);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var session = await _templates.OpenReadAsync(context, CancellationToken.None, availability);
        using var templater = TemplateFixtures.CreateTemplater(session);
        foreach (var raw in ReadNodeSource().Templates)
        {
            var selected = (await templater.ResolveGroupAsync(raw.Id!, TemplateType.Item, CancellationToken.None)).Group!.Single();
            using var source = new NodePolicySourceFixture(selected, FunctionArchive);

            var qualified = await new NodeItemSource().ReadAsync(source, CancellationToken.None);

            qualified.Sources.Should().HaveCount(8);
            foreach (var pin in qualified.Sources)
            {
                pin.TemplateIdentity.Should().Be(selected.Entry.Identity);
                var bytes = await source.ReadPackageFileAsync(pin.RelativePath, CancellationToken.None);
                pin.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes.Span)));
            }
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
        using var source = await ReadQualifiedSourceAsync();
        source.ChangeOriginal(raw => raw["actions"]!.AsArray().Single(action => action!["type"]!.GetValue<string>() == "WriteToFile")!["filePath"] = path);

        Func<Task> qualify = () => new NodeItemSource().ReadAsync(source, CancellationToken.None);

        await qualify.Should().ThrowAsync<InvalidDataException>();
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
        using var source = await ReadQualifiedSourceAsync();
        if (corruption is "secondWrite" or "duplicateAction" or "wrongJob" or "unmatchedBody" or "changedBody")
        {
            source.ChangeOriginal(raw =>
            {
                if (corruption == "secondWrite") raw["actions"]!.AsArray().Add(new JsonObject { ["name"] = "extra", ["type"] = "WriteToFile", ["filePath"] = "extra.js" });
                else if (corruption == "duplicateAction") raw["jobs"]![0]!["actions"]![1] = raw["jobs"]![0]!["actions"]![0]!.DeepClone();
                else if (corruption == "wrongJob") raw["jobs"]![0]!["type"] = "AppendToFile";
                else if (corruption == "unmatchedBody") raw["actions"]![1]!["source"] = "$(OTHER_CONTENT)";
                else raw["files"]!["function.js"] = raw["files"]!["function.js"]!.GetValue<string>() + "\nchanged";
            });
        }
        else if (corruption == "nativeBody")
        {
            source.ChangeNativeBody();
        }
        else
        {
            source.ChangeConfiguration(config =>
            {
                if (corruption == "nativeNameDirectory") config["preferNameDirectory"] = true;
                else if (corruption == "nativeFileRename") config["symbols"]!["timerTrigger-schedule"]!["fileRename"] = "funcNodeItem";
                else if (corruption == "userIntentParameter") config["symbols"]!["InvocationOutputIntent"]!["type"] = "parameter";
                else if (corruption == "foreignIntentBinding") config["symbols"]!["InvocationOutputIntent"]!["binding"] = "host:WorkingDirectory";
                else if (corruption == "foreignContentPath") config["sourceName"] = "../foreign";
                else config["sourceName"] = "OtherNativeName";
            });
        }

        Func<Task> qualify = () => new NodeItemSource().ReadAsync(source, CancellationToken.None);

        await qualify.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task SourceQualifier_Arguments_RejectInvalidSourceAndLanguage()
    {
        Func<Task> missing = () => new NodeItemSource().ReadAsync(null!, CancellationToken.None);
        await missing.Should().ThrowAsync<ArgumentNullException>();
        using var source = await ReadQualifiedSourceAsync();
        source.LanguageOverride = "js";
        Func<Task> invalidLanguage = () => new NodeItemSource().ReadAsync(source, CancellationToken.None);
        await invalidLanguage.Should().ThrowAsync<InvalidDataException>();
        source.LanguageOverride = null;
        source.Dispose();
        Func<Task> expired = () => new NodeItemSource().ReadAsync(source, CancellationToken.None);
        await expired.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceQualifier_ProviderBothModes_EmitsOnlyGenericSourcePinsWithoutChangingRequestedTarget(bool legacy)
    {
        using var source = await ReadQualifiedSourceAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "project", legacy ? "legacy-nested" : "literal-nested"));
        var values = source.CanonicalParameters.ToDictionary(parameter => parameter.Id, parameter => parameter.DefaultValue, StringComparer.Ordinal);
        var invocation = new NodePolicyInvocationFixture(source, output, values);
        NativeTemplatePolicy policy = new NodeNativeTemplatePolicy();

        var prepared = await policy.QualifyInvocationAsync(invocation, CancellationToken.None);

        prepared.Succeeded.Should().BeTrue();
        prepared.RequiredSources.Should().HaveCount(8);
        prepared.CanonicalDefaults.Should().BeEmpty();
        prepared.ProjectBinding.Should().BeNull();
        invocation.OutputDirectory.Should().BeSameAs(output);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    private async Task<NodePolicySourceFixture> ReadQualifiedSourceAsync()
    {
        await InstallNodeAuthoringCatalogAsync();
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None, availability);
        using var templater = TemplateFixtures.CreateTemplater(session);
        var selected = (await templater.ResolveGroupAsync("TimerTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
        return new(selected, FunctionArchive);
    }
}
