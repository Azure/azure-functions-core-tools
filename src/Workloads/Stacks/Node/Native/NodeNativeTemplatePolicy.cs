// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Native;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Node.Native;

/// <summary>
/// Qualifies the Node function source through its actual owning workload registration.
/// </summary>
internal sealed class NodeNativeTemplatePolicy : NativeTemplatePolicy
{
    private readonly NodeItemSource _items = new();

    public override string Stack => "node";

    public override bool Handles(NativeTemplateSource selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return !selected.IsProject && selected.PackageId == NodeItemSource.PackageId
            && selected.Language is "JavaScript" or "TypeScript" && NuGetVersion.TryParse(selected.PackageVersion, out _);
    }

    public override NativeTemplatePolicyStages GetRequestedStages(NativeTemplateSource selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return NativeTemplatePolicyStages.Invocation | NativeTemplatePolicyStages.CapturedOutput;
    }

    public override async Task<NativeItemPolicyDescription> DescribeItemAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
        => (await _items.ReadAsync(selected, cancellationToken)).Description;

    public override Task<IReadOnlyList<string>> ValidateProjectAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(["Node function policy cannot qualify a project template."]);
    }

    public override async Task<NativeTemplateInvocationQualification> QualifyInvocationAsync(
        NativeTemplateInvocationContext invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        await invocation.RevalidateAsync(cancellationToken);
        NodeQualifiedItem item = await _items.ReadAsync(invocation.Source, cancellationToken);
        HashSet<string> names = new(invocation.Source.CanonicalParameters.Select(parameter => parameter.Id), StringComparer.Ordinal);
        if (invocation.CanonicalValues.Keys.Any(key => !names.Contains(key))
            || invocation.ExplicitParameterNames.Any(key => !names.Contains(key) || !invocation.CanonicalValues.ContainsKey(key)))
        {
            return new(false, ["Node qualification requires final declared canonical values and explicit input provenance."],
                new Dictionary<string, string?>(), []);
        }
        return new(true, [], new Dictionary<string, string?>(), item.Sources);
    }

    public override async Task<NativeTemplateCaptureQualification> QualifyCapturedOutputAsync(
        NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);
        await captured.RevalidateAsync(cancellationToken);
        NodeQualifiedItem item = await _items.ReadAsync(captured.Invocation.Source, cancellationToken);
        if (captured.Files.Count != 1 || captured.PrimaryOutputs.Count != 0)
        {
            return new(false, ["Node function creation requires exactly one regular file and no project outputs."]);
        }
        NativeTemplateCapturedFile file = captured.Files[0];
        string relative = file.SourceRelativePath.StartsWith("./", StringComparison.Ordinal) ? file.SourceRelativePath[2..] : file.SourceRelativePath;
        if (relative != Path.GetFileName(item.ContentPath)
            || file.Source.TemplateIdentity != captured.Invocation.Source.Identity
            || file.Source.RelativePath != item.ContentPath
            || !item.Sources.Contains(file.Source))
        {
            return new(false, ["The native Node output is not bound to its exact selected source body."]);
        }
        return new(true, []);
    }

    public override INativeTemplateAppendCapability CreateAppendCapability(NativeTemplateSource selected, WorkingDirectory invocationOutputDirectory)
        => throw new NotSupportedException("The owned Node function catalog has no append operation.");
}
