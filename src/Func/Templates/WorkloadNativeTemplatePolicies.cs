// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Keeps actual policy/source admissions within one command without selecting replacement content.
/// </summary>
internal sealed class WorkloadNativeTemplatePolicies(
    NativeTemplatePolicyAdmission admission, IFileSystem files, IInteractionService interaction, ITemplateArgumentParser arguments)
{
    private readonly NativeTemplatePolicyAdmission _admission = admission ?? throw new ArgumentNullException(nameof(admission));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly Dictionary<SourceKey, NativeTemplatePolicyCandidate> _candidates = [];

    public bool HasPolicyForStack(string stack) => _admission.HasPolicyForStack(stack);

    public async Task<NativeTemplatePolicyCandidate?> QualifyProjectAsync(
        NativeProjectTemplateSource source, bool requireLayout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        NativeTemplatePolicyCandidate? candidate = await GetCandidateAsync(source.Entry, source.Configuration,
            source.ConfigurationSourceIdentity, source.PackageMountPoint, source.ReadPackageFileAsync,
            source.RevalidateAsync, requireLayout, cancellationToken, source.SessionIdentity);
        return candidate;
    }

    public async Task<WorkloadNativeTemplatePolicyAdapter?> FindAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Configuration is not { } configuration || selected.ConfigurationSourceIdentity is null)
        {
            return null;
        }
        NativeTemplatePolicyCandidate? candidate = await GetCandidateAsync(selected.Entry, configuration,
            selected.ConfigurationSourceIdentity, selected.PackageMountPoint, selected.ReadPackageFileAsync,
            selected.ValidateConfigurationAsync, requireLayout: false, cancellationToken, selected.InvocationOwner);
        return candidate is null ? null : new(candidate, FindAsync, _files, _interaction, _arguments);
    }

    internal async Task<NativeTemplatePolicyCandidate?> GetCandidateAsync(
        TemplateCatalogEntry entry, TemplateConfigurationSnapshot configuration, string sourceIdentity, string? mountPoint,
        Func<string, CancellationToken, Task<ReadOnlyMemory<byte>>> read,
        Func<CancellationToken, Task<TemplateInvocationResult?>>? revalidate,
        bool requireLayout, CancellationToken cancellationToken, object? sessionIdentity = null)
    {
        if (mountPoint is null || revalidate is null)
        {
            throw new InvalidTemplateMetadataException("Policy qualification requires the same captured mount and source revalidator.");
        }
        SourceKey key = new(sessionIdentity ?? throw new InvalidTemplateMetadataException("Policy admission requires its captured session identity."),
            entry.Identity, entry.Package, sourceIdentity, configuration.Fingerprint,
            configuration.HostConfigPlace, configuration.HostData);
        try
        {
            if (_candidates.TryGetValue(key, out NativeTemplatePolicyCandidate? existing))
            {
                if (requireLayout && !existing.Admission.Required)
                {
                    throw new InvalidDataException("The required project layout was not included in this source admission. Rerun selection.");
                }
                await existing.RevalidateDeclarationAsync(cancellationToken);
                return existing;
            }
            BoundNativeTemplatePolicy? owner = await _admission.TryBindAsync(entry, mountPoint, cancellationToken, required: requireLayout);
            if (owner is null)
            {
                if (requireLayout)
                {
                    throw new InvalidDataException("This project requires an authorized owning workload layout policy. Update the owning workload and Azure Functions CLI.");
                }
                return null;
            }
            bool binding = configuration.Root.TryGetProperty("symbols", out JsonElement symbols) && symbols.ValueKind == JsonValueKind.Object
                && symbols.EnumerateObject().Any(symbol => symbol.Value.ValueKind == JsonValueKind.Object
                    && symbol.Value.TryGetProperty("binding", out JsonElement value) && value.ValueKind == JsonValueKind.String
                    && value.GetString()!.StartsWith("msbuild:", StringComparison.OrdinalIgnoreCase));
            NativeTemplatePolicyCandidate candidate = new(owner, entry, configuration.Fingerprint, sourceIdentity, read, revalidate,
                configuration.PackageConfigurationPath ?? throw new InvalidDataException("The selected configuration has no captured package-relative path."), binding);
            if (!await candidate.QualifySourceAsync(requireLayout, cancellationToken))
            {
                return null;
            }
            _candidates.Add(key, candidate);
            return candidate;
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidTemplateMetadataException(exception.Message, exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidTemplateMetadataException(exception.Message, exception);
        }
    }

    private sealed record SourceKey(object SessionIdentity, string Identity, TemplatePackageInfo? Package, string SourceIdentity,
        string ConfigurationSha256, string? HostPath, string? HostData);
}
