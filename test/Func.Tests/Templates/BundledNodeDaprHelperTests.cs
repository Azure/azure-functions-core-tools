// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.DependencyInjection;
using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "Azure.Functions.Node.Http.JavaScript", "Foo_Invoke")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "DaprServiceInvocationTrigger-JavaScript", "Foo_Invoke")]
    [InlineData("DaprPublishOutputBinding-JavaScript", "Azure.Functions.Node.Http.JavaScript", "Foo_Topic")]
    [InlineData("DaprPublishOutputBinding-JavaScript", "DaprPublishOutputBinding-JavaScript", "Foo_Topic")]
    public async Task DaprHelpers_CliReachableCrossRoleNames_DoNotOverlapPrimaryAndHelperDomains(
        string firstIdentity, string secondIdentity, string secondName)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var natives = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        var creator = new TemplateCreator(session.Settings);
        string target = Path.Combine(_root, "cross-role");
        foreach (var (identity, name) in new[] { (firstIdentity, "Foo"), (secondIdentity, secondName) })
        {
            var native = natives.Single(item => item.Identity == identity);
            var created = await creator.InstantiateAsync(native, name, name, target, new Dictionary<string, string?>(),
                cancellationToken: CancellationToken.None);
            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        }
        Directory.GetFiles(target).Select(Path.GetFileName).Should().BeEquivalentTo("Foo.js", secondName + ".js");
    }

    [Theory]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "host")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "Host")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "hOsT")]
    [InlineData("DaprPublishOutputBinding-JavaScript", "host")]
    [InlineData("DaprPublishOutputBinding-JavaScript", "Host")]
    [InlineData("DaprPublishOutputBinding-JavaScript", "hOsT")]
    public async Task DaprHelpers_ReservedHostName_RefusesPreviewAndCreationBeforeWrites(string identity, string name)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var services = await CreateNodeWorkflowServicesAsync(new NodeNpmInstaller());
        using var scope = services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string project = Path.Combine(_root, "reserved-project");
        (await composer.ExecuteProjectAsync(NodeComposedInit(project, "js", ["--skip-npm-install", "true"]), CancellationToken.None))
            .Succeeded.Should().BeTrue();
        var before = Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var command = NodeComposedItem(Path.Combine(project, "src", "functions"), identity) with { Name = name };

        var preview = await composer.ExecuteItemAsync(command with { DryRun = true }, CancellationToken.None);
        var created = await composer.ExecuteItemAsync(command, CancellationToken.None);

        preview.Succeeded.Should().BeFalse();
        created.Succeeded.Should().BeFalse();
        preview.Invocation.Should().BeNull();
        created.Invocation.Should().BeNull();
        created.Diagnostics.Should().ContainSingle().Which.Should().Contain("reserved").And.Contain("Nothing was generated");
        Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("DaprPublishOutputBinding-JavaScript", "-Topic")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "-Invoke")]
    public async Task DaprHelpers_ActualNativeBackend_TwoDifferentNamesHaveDistinctAssociatedRegistrationsAndRoutes(string identity, string suffix)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var native = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single(item => item.Identity == identity);
        var creator = new TemplateCreator(session.Settings);
        string target = Directory.CreateDirectory(Path.Combine(_root, "dapr-output")).FullName;
        string sentinel = Path.Combine(target, "user.js");
        File.WriteAllText(sentinel, "// preserve user source\n");
        foreach (string name in new[] { "FirstMixedName", "SecondMixedName" })
        {
            var preview = await creator.InstantiateAsync(native, name, name, target, new Dictionary<string, string?>(),
                dryRun: true, cancellationToken: CancellationToken.None);
            File.Exists(Path.Combine(target, name + ".js")).Should().BeFalse();
            var created = await creator.InstantiateAsync(native, name, name, target, new Dictionary<string, string?>(),
                cancellationToken: CancellationToken.None);

            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
            created.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath).Should()
                .Equal(preview.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath));
        }
        if (suffix == "-Invoke")
        {
            foreach (string name in new[] { "FirstMixedName", "SecondMixedName" })
            {
                string source = File.ReadAllText(Path.Combine(target, name + ".js"));
                source.Should().Contain("--method " + name + " ", "/api/" + name + "/invoke/functionapp/" + name + "'");
                source.Should().NotContain("{yourFunctionName}", "InvokeOutputBinding", "funcNodeItem");
            }
        }
        File.ReadAllText(sentinel).Should().Be("// preserve user source\n");
        Directory.GetFiles(target).Select(Path.GetFileName).Should().BeEquivalentTo("user.js", "FirstMixedName.js", "SecondMixedName.js");
    }

    [Theory]
    [InlineData("DaprPublishOutputBinding-JavaScript", 122, true)]
    [InlineData("DaprPublishOutputBinding-JavaScript", 123, false)]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", 121, true)]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", 122, false)]
    public async Task DaprHelpers_SelectedCompatibility_ValidatesDerivedNameLengthBeforePreviewOrMutation(string identity, int length, bool succeeds)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync(identity);
        string name = new('A', length);

        string? diagnostic = NodeItemSourceQualifier.ValidateDaprInvocation(source, name);

        if (succeeds) diagnostic.Should().BeNull();
        else diagnostic.Should().Contain("at most " + (length - 1)).And.Contain("Nothing was generated");
    }

    [Theory]
    [InlineData("DaprPublishOutputBinding-JavaScript", 121, true)]
    [InlineData("DaprPublishOutputBinding-JavaScript", 122, true)]
    [InlineData("DaprPublishOutputBinding-JavaScript", 123, false)]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", 120, true)]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", 121, true)]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", 122, false)]
    public async Task DaprHelpers_ProductionWorkflow_MaximumNameBoundaryPreservesPreviewAndNoWriteRefusal(
        string identity, int length, bool succeeds)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var services = await CreateNodeWorkflowServicesAsync(new NodeNpmInstaller());
        using var scope = services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string project = Path.Combine(_root, "dapr-project");
        (await composer.ExecuteProjectAsync(NodeComposedInit(project, "js", ["--skip-npm-install", "true"]), CancellationToken.None))
            .Succeeded.Should().BeTrue();
        string target = Path.Combine(project, "src", "functions");
        string name = new('A', length);
        var command = NodeComposedItem(target, identity) with { Name = name };
        var before = Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

        var preview = await composer.ExecuteItemAsync(command with { DryRun = true }, CancellationToken.None);
        Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes).Should().BeEquivalentTo(before);
        var created = await composer.ExecuteItemAsync(command, CancellationToken.None);

        preview.Succeeded.Should().Be(succeeds, string.Join("; ", preview.Diagnostics));
        created.Succeeded.Should().Be(succeeds, string.Join("; ", created.Diagnostics));
        if (succeeds)
        {
            created.Invocation!.Changes.Should().BeEquivalentTo(preview.Invocation!.Changes);
            string content = File.ReadAllText(Path.Combine(target, name + ".js"));
            content.Should().Contain(name + (identity.StartsWith("DaprPublish", StringComparison.Ordinal) ? "-Topic" : "-Invoke"));
        }
        else
        {
            preview.Invocation.Should().BeNull();
            created.Invocation.Should().BeNull();
            created.Diagnostics.Should().ContainSingle().Which.Should().Contain("at most").And.Contain("Nothing was generated");
            Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes).Should().BeEquivalentTo(before);
        }
    }

    [Theory]
    [InlineData("DaprPublishOutputBinding-JavaScript")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript")]
    public async Task DaprHelpers_HistoricalFixedEncoding_QualifiesProvenanceButRefusesSelectedGenerationWithUpdateGuidance(string identity)
    {
        NodeItemSource current = await ReadQualifiedSourceAsync(identity);
        NodeItemSource historical = current with
        {
            Content = Encoding.UTF8.GetBytes(current.Template.Files!.Single().Value.Replace("$(FUNCTION_NAME_INPUT)", "funcNodeItem", StringComparison.Ordinal)),
        };
        NodeItemSourceQualifier.Validate(historical, "JavaScript");
        const string hint = "Run 'func workload update Selected.Physical.Node --version 1.0.0 --exact'.";

        Action validate = () => NodeItemSourceQualifier.ValidateDaprInvocation(historical, historical.Contract.DefaultName);

        var failure = validate.Should().Throw<NodeItemHelperCompatibilityException>().Which;
        var compatibility = new NodeItemCompatibility(_interaction, () => hint);
        compatibility.DescribeUnsupportedHelpers(failure).Should().Contain(hint).And.Contain(identity)
            .And.Contain("does not separate").And.Contain("Nothing was generated").And.Contain("no workload was upgraded automatically");
    }

    [Theory]
    [InlineData("DaprPublishOutputBinding-JavaScript", "-Topic", "_Topic")]
    [InlineData("DaprServiceInvocationTrigger-JavaScript", "-Invoke", "_Invoke")]
    public async Task DaprHelpers_PreviousUnderscoreEncoding_RequiresExplicitUpdateRatherThanReintroducingCrossRoleCollisions(
        string identity, string currentSuffix, string previousSuffix)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync(identity);
        source = source with
        {
            Content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source.Content.Span).Replace(
                "funcNodeItem" + currentSuffix, "funcNodeItem" + previousSuffix, StringComparison.Ordinal)),
        };

        NodeItemSourceQualifier.Validate(source, "JavaScript");
        Action invoke = () => NodeItemSourceQualifier.ValidateDaprInvocation(source, "Foo");

        invoke.Should().Throw<NodeItemHelperCompatibilityException>().WithMessage("*does not separate*");
    }

    [Theory]
    [InlineData("hosts")]
    [InlineData("HostFunction")]
    [InlineData("myhost")]
    public async Task DaprHelpers_ReservedNamePredicate_DoesNotRejectNonreservedNames(string name)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync("DaprServiceInvocationTrigger-JavaScript");

        string? diagnostic = NodeItemSourceQualifier.ValidateDaprInvocation(source, name);

        diagnostic.Should().BeNull();
    }

    [Theory]
    [InlineData("helperName")]
    [InlineData("routeOnly")]
    [InlineData("commentCaller")]
    [InlineData("rawCommentDecoy")]
    [InlineData("extraSymbol")]
    [InlineData("foreignNameForm")]
    public async Task DaprHelpers_SourceQualification_RefusesPartialOrUnreviewedAssociation(string corruption)
    {
        NodeItemSource source = await ReadQualifiedSourceAsync("DaprServiceInvocationTrigger-JavaScript");
        string native = Encoding.UTF8.GetString(source.Content.Span);
        if (corruption == "helperName") native = native.Replace("funcNodeItem-Invoke", "OtherHelper", StringComparison.Ordinal);
        else if (corruption == "routeOnly") native = native.Replace("funcNodeItem/invoke/{appId}/{methodName}", "invoke/{appId}/{methodName}", StringComparison.Ordinal);
        else if (corruption == "commentCaller") native = native.Replace("--method funcNodeItem", "--method OtherMain", StringComparison.Ordinal);
        else if (corruption == "rawCommentDecoy")
        {
            source.Template.Files!["function.js"] += "\n// app.http('InvokeOutputBinding', {\n";
            native += "\n// app.http('funcNodeItem-Invoke', {\n";
        }
        else if (corruption == "extraSymbol")
        {
            var config = System.Text.Json.Nodes.JsonNode.Parse(source.Configuration.Span)!;
            config["symbols"]!["Other"] = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"parameter","datatype":"string","replaces":"Invoke"}""");
            source = source with { Configuration = Encoding.UTF8.GetBytes(config.ToJsonString()) };
        }
        else
        {
            var config = System.Text.Json.Nodes.JsonNode.Parse(source.Configuration.Span)!;
            config["forms"] = System.Text.Json.Nodes.JsonNode.Parse("""{"identity":{"identifier":"lowerCase"}}""");
            source = source with { Configuration = Encoding.UTF8.GetBytes(config.ToJsonString()) };
        }
        source = source with { Content = Encoding.UTF8.GetBytes(native) };

        Action qualify = () => NodeItemSourceQualifier.Validate(source, "JavaScript");

        qualify.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void DaprHelpers_Contracts_RejectMissingSourceAndMissingFailure()
    {
        Action missingSource = () => NodeItemSourceQualifier.ValidateDaprInvocation(null!, "Example");
        var compatibility = new NodeItemCompatibility(_interaction);
        Action missingFailure = () => compatibility.DescribeUnsupportedHelpers(null!);

        missingSource.Should().Throw<ArgumentNullException>();
        missingFailure.Should().Throw<ArgumentNullException>();
    }

}
