// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;
using Azure.Functions.Cli.Tests.Templates.PackageIntake;
using Azure.Functions.Cli.Tests.Templates.Preparation;
using Azure.Functions.Cli.Tests.Templates.Selection;
using Azure.Functions.Cli.Workers;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

/// <summary>
/// Composes real shared components with a fixture adapter and captured project, not producer-qualified content.
/// </summary>
internal sealed class PackageTemplateFixture
{
    internal const string Capability = "publisher.future-model.http";
    private const string FixturePackageType = "IntegrationFixture";

    public PackageTemplateFixture()
    {
        Storage.FileSystem.OpenReadIfExists(TemplatePackageTestData.ArchivePath)
            .Returns(_ => new MemoryStream(ArchiveBytes, writable: false));
        ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ProjectResolutionResult>(new ProjectResolutionResult.Resolved(Project, "Fixture project resolved.")));
        Adapter.OpenAsync(Arg.Any<TemplatePackageContentLease>(), Arg.Any<TemplateEngineContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Lease = call.ArgAt<TemplatePackageContentLease>(0);
                Context = call.ArgAt<TemplateEngineContext>(1);
                AdapterToken = call.ArgAt<CancellationToken>(2);
                return Open is null ? Task.FromResult<PackageTemplateSession>(Session) : Open(Lease, Context, AdapterToken);
            });
        Application = new(
            new(Storage.FileSystem, TemplatePackageTestData.Limits()),
            Adapter,
            new(new()),
            ProjectResolver,
            new ItemTemplateCompatibilityMatcher(),
            Storage.FileSystem);
    }

    public byte[] ArchiveBytes { get; set; } = TemplatePackageTestData.Archive(
        manifest: TemplatePackageTestData.Manifest(types: ["Template", FixturePackageType]));

    public PreparedOperationTestFileSystem Storage { get; } = new();

    public FixtureProject Project { get; set; } = new();

    public IFunctionsProjectResolver ProjectResolver { get; } = Substitute.For<IFunctionsProjectResolver>();

    public IPackageTemplateAdapter Adapter { get; } = Substitute.For<IPackageTemplateAdapter>();

    public FixtureSession Session { get; set; } = new([]);

    public TemplatePackageContentLease? Lease { get; private set; }

    public TemplateEngineContext? Context { get; private set; }

    public CancellationToken AdapterToken { get; private set; }

    public Func<TemplatePackageContentLease, TemplateEngineContext, CancellationToken, Task<PackageTemplateSession>>? Open { get; set; }

    public PackageTemplateApplication Application { get; }

    public LocalTemplatePackageRequest Package => TemplatePackageTestData.Request(type: FixturePackageType);

    public PackageTemplateRequest Request(
        TemplateType type = TemplateType.Project,
        string? reference = null,
        ImmutableArray<string> tokens = default,
        string? name = null)
        => new(Package, new(reference ?? Reference(type), type, tokens.IsDefault ? [] : tokens, []),
            PreparedOperationTestFileSystem.Target, name);

    public FixtureCandidate Candidate(
        TemplateType type = TemplateType.Project,
        string? identity = null,
        string? requirement = Capability,
        FuncTemplateConstraintOutcome eligibility = FuncTemplateConstraintOutcome.Eligible,
        params TemplateParameterDefinition[] parameters)
    {
        TemplateCatalogEntry metadata = SelectionFixtures.Metadata(identity ?? "Fixture." + type) with
        {
            Type = type,
            ShortNames = [Reference(type)],
            Parameters = parameters,
        };
        string? declaration = type == TemplateType.Item && requirement is not null
            ? $$"""{"type":"func-project-capabilities","args":["{{requirement}}"]}"""
            : null;
        return new(new(metadata, declaration), eligibility);
    }

    public void SetCandidates(params FixtureCandidate[] candidates) => Session = new(candidates);

    private static string Reference(TemplateType type) => type == TemplateType.Project ? "fixture-project" : "fixture-item";
}

internal sealed class FixtureSession(IEnumerable<PackageTemplateCandidate> candidates) : PackageTemplateSession(candidates)
{
    public int DisposeCalls { get; private set; }

    public Exception? CleanupFailure { get; set; }

    public override ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return CleanupFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(CleanupFailure);
    }
}

internal sealed class FixtureCandidate(TemplateDescriptor descriptor, FuncTemplateConstraintOutcome eligibility)
    : PackageTemplateCandidate(descriptor, eligibility, ["Fixture native eligibility diagnostic."])
{
    public int RenderCalls { get; private set; }

    public TemplateArgumentParseResult? Arguments { get; private set; }

    public string? Name { get; private set; }

    public CancellationToken RenderToken { get; private set; }

    public Func<CancellationToken, Task>? DuringRender { get; set; }

    public PackageTemplateCapture Capture { get; set; } = (descriptor ?? throw new ArgumentNullException(nameof(descriptor))).Metadata.Type == TemplateType.Project
        ? new([new("host.json", PreparedOutputKind.File, "{}"u8), new("app.bin", PreparedOutputKind.File, [0, 1, 255])],
            new(".", """{"stack":{"runtime":"fixture","language":"fixture"}}"""u8))
        : new([new("functions/http.js", PreparedOutputKind.File, Encoding.UTF8.GetBytes("fixture function bytes"))]);

    protected override async Task<PackageTemplateCapture> RenderCoreAsync(
        TemplateArgumentParseResult arguments, string? name, CancellationToken cancellationToken)
    {
        RenderCalls++;
        Arguments = arguments;
        Name = name;
        RenderToken = cancellationToken;
        if (DuringRender is not null)
        {
            await DuringRender(cancellationToken);
        }

        return Capture;
    }
}

internal sealed class FixtureProject(IEnumerable<ProjectCapability> capabilities) : FunctionsProject(capabilities)
{
    public FixtureProject()
        : this([new(PackageTemplateFixture.Capability, ProjectCapabilitySupport.Supported)])
    {
    }

    public string Root { get; init; } = PreparedOperationTestFileSystem.Target;

    public override WorkingDirectory WorkingDirectory => new(new(Root), WasExplicit: true);

    public override string StackName => "fixture-runtime";

    public override string StackDisplayName => "Fixture runtime";

    public override string Language => "Fixture language";

    public override bool SupportsExtensionBundles => false;

    public override FunctionsWorkerReference WorkerReference => FunctionsWorkerReference.FromWorkload("fixture-runtime");

}
