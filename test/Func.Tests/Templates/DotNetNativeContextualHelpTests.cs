// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using Shared = Azure.Functions.Cli.Templates.DotNet.Shared;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetNativeContextualHelpTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-context-help-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows can keep the installed collectible workload assembly mapped until test process shutdown.
        }
    }

    [Theory]
    [InlineData("unqualified")]
    [InlineData("changed")]
    [InlineData("unavailableProject")]
    public async Task DescribeHelp_UnqualifiedSourceOrProject_RemovesUnevaluatedDefaultWithoutSdkWork(string failure)
    {
        IFunctionsProjectResolver projects = Substitute.For<IFunctionsProjectResolver>();
        Shared.IDotnetCliRunner runner = Substitute.For<Shared.IDotnetCliRunner>();
        DotNetTemplateWorkflowProvider provider = new DotNetTemplateWorkflowComposer(projects, new PhysicalFileSystem(),
            runner, new Shared.FSharpProjectFiles()).Create();
        TemplateParameterDefinition parameter = new("namespace", "namespace", null, "Original namespace", "string",
            [], "Company.Function", false, false, false);
        TemplateCatalogEntry entry = new("Azure.Function.CSharp.Isolated.HttpTrigger.3.x", "HTTP", null, TemplateType.Item,
            ["http"], null, "C#", 0, new("Azure.Functions.Templates.DotNet", "1.0.0"), false, [parameter], null)
        {
            ConstraintEvaluation = new(TemplateConstraintEvaluationStatus.Eligible, []),
        };
        string fingerprint = failure == "unqualified" ? new string('0', 64)
            : "65B1EC29BE5F6176FF0C8ECC1755683970E3D2F69089A709AFB3E593924F6CD7";
        ResolvedTemplate selected = new(entry, (_, _) => throw new InvalidOperationException("Help must not invoke the selected item."),
            configurationSha256: fingerprint,
            validateConfiguration: _ => Task.FromResult<TemplateInvocationResult?>(failure == "changed"
                ? new(TemplateInvocationStatus.TemplateChanged, [], "changed") : null));
        projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.NotResolved("The selected project is unavailable."));
        var directory = WorkingDirectory.FromExplicit(_root);

        NativeItemHelpProjection result = await provider.DescribeHelpAsync(new(selected, new(directory, new(directory, "dotnet", "C#"))),
            CancellationToken.None);

        result.Diagnostics.Should().ContainSingle();
        result.Parameters.Should().ContainSingle().Which.Should().Be(parameter with
        {
            DefaultValue = null,
            Description = "The contextual default is unevaluated. Supply an explicit value or repair the selected .NET project and retry help.",
        });
        if (failure != "unavailableProject")
        {
            projects.ReceivedCalls().Should().BeEmpty();
        }
        runner.ReceivedCalls().Should().BeEmpty();
    }
}
