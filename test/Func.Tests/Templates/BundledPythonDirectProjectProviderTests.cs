// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Python;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Fact]
    public async Task DirectProjectProvider_ActualOwnedBasic_QualifiesAndReturnsUnchangedReadyInvocationWithoutPreparation()
    {
        await InstallAsync();
        using var templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync("basic", TemplateType.Project)).Group!.Single();
        var provider = CreatePythonProvider();
        var source = await ReadDirectProjectSourceAsync(selected);
        var context = DirectProjectContext(templater, selected);

        var qualification = await provider.QualifyLayoutAsync(source, CancellationToken.None);
        var prepared = await provider.PrepareAsync(context, CancellationToken.None);

        selected.Entry.Identity.Should().Be("Azure.Functions.Python.Basic");
        selected.Entry.Package!.Identifier.Should().Be("Azure.Functions.Templates.Python");
        source.ConfigurationSourceIdentity.Should().Be(selected.ConfigurationSourceIdentity);
        provider.Handles(selected.Entry).Should().BeTrue();
        qualification.Should().BeNull();
        ((INativeProjectWorkflowProvider)provider).Preparation.Should().BeNull();
        prepared.Diagnostics.Should().BeEmpty();
        prepared.Request!.OutputDirectory.Should().Be(context.Invocation.OutputDirectory);
        prepared.Request.Name.Should().Be(context.Invocation.Name);
        prepared.Request.Parameters.Should().BeEquivalentTo(context.Invocation.Parameters);
        prepared.Request.PolicyCandidate.Should().NotBeNull();
        Directory.Exists(context.Invocation.OutputDirectory.Info.FullName).Should().BeFalse();
        var created = await selected.InvokeAsync(prepared.Request!, CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        var expectedRequirements = await selected.ReadPackageFileAsync("content/project/requirements.txt", CancellationToken.None);
        File.ReadAllBytes(Path.Combine(context.Invocation.OutputDirectory.Info.FullName, "requirements.txt"))
            .Should().Equal(expectedRequirements.ToArray());
        Directory.Exists(Path.Combine(context.Invocation.OutputDirectory.Info.FullName, ".venv")).Should().BeFalse();
    }

    [Theory]
    [InlineData("requirements")]
    [InlineData("packageVersion")]
    [InlineData("projectIdentity")]
    [InlineData("configuration")]
    public async Task DirectProjectProvider_ChangedOwnedSourceOrIdentity_RefusesLayoutAndPreparationBeforeNativeCreation(string mismatch)
    {
        using (var archive = ZipFile.Open(_archive, ZipArchiveMode.Update))
        {
            var path = mismatch switch
            {
                "requirements" => "content/project/requirements.txt",
                "configuration" => "content/project/.template.config/template.json",
                _ => "source/applications.json",
            };
            string content;
            using (StreamReader reader = new(archive.GetEntry(path)!.Open()))
            {
                content = reader.ReadToEnd();
            }
            if (path == "source/applications.json")
            {
                var map = JsonNode.Parse(content)!;
                if (mismatch == "packageVersion")
                {
                    map["version"] = "99.0.0";
                }
                else
                {
                    map["project"]!["templateIdentity"] = "Azure.Functions.Python.Other";
                }
                content = map.ToJsonString();
            }
            else
            {
                content += mismatch == "configuration" ? "\n" : "\n# changed owned source\n";
            }
            archive.GetEntry(path)!.Delete();
            using StreamWriter writer = new(archive.CreateEntry(path).Open());
            writer.Write(content);
        }
        await InstallAsync();
        using var templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync("basic", TemplateType.Project)).Group!.Single();
        var provider = CreatePythonProvider();
        var source = await ReadDirectProjectSourceAsync(selected);
        var context = DirectProjectContext(templater, selected);

        Func<Task> qualify = () => provider.QualifyLayoutAsync(source, CancellationToken.None);
        await qualify.Should().ThrowAsync<InvalidTemplateMetadataException>().WithMessage("*owned sources*");
        var prepared = await provider.PrepareAsync(context, CancellationToken.None);

        prepared.Request.Should().BeNull();
        prepared.Diagnostics.Should().ContainSingle().Which.Should().Contain("owned sources");
        if (mismatch == "requirements")
        {
            prepared.Diagnostics.Single().Should().Contain("content/project/requirements.txt");
        }
        Directory.Exists(context.Invocation.OutputDirectory.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task DirectProjectProvider_PreCanceled_RefusesBeforeQualificationPreparationOrNativeCreation()
    {
        await InstallAsync();
        using var templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync("basic", TemplateType.Project)).Group!.Single();
        var provider = CreatePythonProvider();
        var source = await ReadDirectProjectSourceAsync(selected);
        var context = DirectProjectContext(templater, selected);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> qualify = () => provider.QualifyLayoutAsync(source, cancellation.Token);
        Func<Task> prepare = () => provider.PrepareAsync(context, cancellation.Token);

        await qualify.Should().ThrowAsync<OperationCanceledException>();
        await prepare.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(context.Invocation.OutputDirectory.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task DirectProjectProvider_NullContracts_AreRejected()
    {
        Action construct = () => new PythonTemplatePolicy(null!, new PythonTemplateAppendExecutor(), new());
        var provider = CreatePythonPolicy();
        Action handles = () => provider.Handles(null!);
        Func<Task> qualify = () => provider.ValidateProjectAsync(null!, CancellationToken.None);
        Func<Task> prepare = () => provider.QualifyInvocationAsync(null!, CancellationToken.None);
        Func<Task> capture = () => provider.QualifyCapturedOutputAsync(null!, CancellationToken.None);

        construct.Should().Throw<ArgumentNullException>();
        handles.Should().Throw<ArgumentNullException>();
        await qualify.Should().ThrowAsync<ArgumentNullException>();
        await prepare.Should().ThrowAsync<ArgumentNullException>();
        await capture.Should().ThrowAsync<ArgumentNullException>();
    }

    private static async Task<NativeProjectTemplateSource> ReadDirectProjectSourceAsync(
        ResolvedTemplate selected, CancellationToken cancellationToken = default)
    {
        var bytes = await selected.ReadPackageFileAsync("content/project/.template.config/template.json", cancellationToken);
        TemplateConfigurationSnapshot configuration = new(Encoding.UTF8.GetString(bytes.Span), selected.ConfigurationSha256!,
            TemplateType.Project, []);
        return new(selected.Entry, configuration, selected.ConfigurationSourceIdentity!, selected.ReadPackageFileAsync);
    }

    private NativeProjectWorkflowContext DirectProjectContext(Templater templater, ResolvedTemplate selected)
    {
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "direct-project"));
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        InitExecutionRequest command = new(output, "basic", "Sample", "python", "Python", false, true, false, false, [], new HashSet<string>());
        return new(templater, selected, arguments, command, new(output, arguments.ValuesByCanonicalName, "Sample"));
    }
}
