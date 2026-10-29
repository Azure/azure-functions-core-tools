// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Selection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates.Integration;

internal interface IPublicPackageTemplates
{
    public bool IsEnabled { get; }

    public bool HandlesProject(string stack, string? language);

    public bool IsPackageReference(string? reference);

    public Task<bool> IsNodeJavaScriptAsync(WorkingDirectory directory, CancellationToken cancellationToken);

    public Task<int> InitializeAsync(WorkingDirectory directory, CancellationToken cancellationToken);

    public Task<int> CreateAsync(NewInvocation invocation, CancellationToken cancellationToken);

    public Task<TemplateDescriptor> ReadItemAsync(WorkingDirectory directory, CancellationToken cancellationToken);

    public Task<bool> IsCompatibleAsync(WorkingDirectory directory, TemplateDescriptor item, CancellationToken cancellationToken);

    public FunctionTemplateInfo ProjectItem(TemplateCatalogEntry metadata);
}

/// <summary>
/// Activates only the explicitly bound qualification cohort; other public routes remain with the existing orchestrator.
/// </summary>
internal sealed class PublicPackageTemplates(
    IOptions<PublicTemplatePackageOptions> options,
    IOptionsMonitor<StackOptions> stacks,
    IProfileResolver profiles,
    IInteractionService interaction,
    ITemplatePackageResources resources,
    LocalTemplatePackageIntake intake,
    IPackageTemplateAdapter adapter,
    PackageTemplateApplication application,
    PackageTemplateCommandExecutor executor,
    ItemTemplateCompatibilityMatcher matcher) : IPublicPackageTemplates
{
    internal const string EngineId = "native-qualification";
    private readonly PublicTemplatePackageBinding? _binding = (options ?? throw new ArgumentNullException(nameof(options))).Value.Qualification;
    private readonly IOptionsMonitor<StackOptions> _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IProfileResolver _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly ITemplatePackageResources _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    private readonly LocalTemplatePackageIntake _intake = intake ?? throw new ArgumentNullException(nameof(intake));
    private readonly IPackageTemplateAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    private readonly PackageTemplateApplication _application = application ?? throw new ArgumentNullException(nameof(application));
    private readonly PackageTemplateCommandExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly ItemTemplateCompatibilityMatcher _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));

    public bool IsEnabled => _binding is not null;

    public bool HandlesProject(string stack, string? language)
        => IsEnabled && string.Equals(stack, "node", StringComparison.OrdinalIgnoreCase)
            && string.Equals(language, "JavaScript", StringComparison.OrdinalIgnoreCase);

    public bool IsPackageReference(string? reference)
        => _binding is { } binding && (string.Equals(reference, binding.PublicItemAlias, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reference, binding.ItemIdentity, StringComparison.Ordinal)
            || string.Equals(reference, binding.ProjectIdentity, StringComparison.Ordinal)
            || string.Equals(reference, "func-http-js", StringComparison.OrdinalIgnoreCase));

    public async Task<bool> IsNodeJavaScriptAsync(WorkingDirectory directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled)
        {
            return false;
        }

        await _profiles.ResolveAsync(new(directory.Info, RequestedProfileName: null, CanPrompt: _interaction.IsInteractive), cancellationToken);
        StackOptions stack = _stacks.Get(directory.Info.FullName);
        return HandlesProject(stack.Runtime ?? string.Empty, stack.Language);
    }

    public Task<int> InitializeAsync(WorkingDirectory directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        PublicTemplatePackageBinding binding = Binding();
        TemplatePreparationRequest request = new(
            new(binding.ProjectIdentity, TemplateType.Project, [], []), directory.Info.FullName)
        {
            RequiredProjectDefault = binding.ProjectIdentity,
        };
        _interaction.WriteHint("Preview5 qualification project: package-backed, create-only generation. Dependency restore runs with 'func start'.");
        return _executor.ExecuteAsync("init", request, json: false, preview: false,
            token => PrepareAsync(request, token), cancellationToken);
    }

    public Task<int> CreateAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        PublicTemplatePackageBinding binding = Binding();
        RejectForce(invocation.Force);
        ValidateOutput(invocation.OutputFormat);

        if (invocation.RepeatedHostOptions.Length != 0)
        {
            throw new GracefulException($"Package-backed options cannot be repeated: {string.Join(", ", invocation.RepeatedHostOptions)}.", isUserError: true);
        }

        string reference = string.Equals(invocation.RequestedTemplate, binding.ProjectIdentity, StringComparison.Ordinal)
            ? binding.ProjectIdentity : binding.ItemIdentity;
        TemplatePreparationRequest request = new(
            new(reference, TemplateType.Item, invocation.TemplateArguments, invocation.ReservedAliases),
            invocation.WorkingDirectory.Info.FullName, invocation.RequestedFunctionName)
        {
            RequiredProjectDefault = binding.ProjectIdentity,
        };
        if (!invocation.JsonOutput)
        {
            _interaction.WriteHint("Preview5 qualification HTTP: package-backed and create-only. Other templates retain the existing route.");
        }

        return _executor.ExecuteAsync("new", request, invocation.JsonOutput, preview: false,
            token => PrepareAsync(request, token), cancellationToken);
    }

    public async Task<TemplateDescriptor> ReadItemAsync(WorkingDirectory directory, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadItemCoreAsync(directory, cancellationToken);
        }
        catch (InvalidTemplatePackageException exception)
        {
            throw new GracefulException($"Bound template package validation failed: {exception.Message}", exception, isUserError: true);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            throw new GracefulException($"Bound native metadata validation failed: {exception.Message}", exception, isUserError: true);
        }
        catch (TemplatePreparationException exception)
        {
            throw new GracefulException($"{exception.Failure}: {exception.Message}", exception, isUserError: true);
        }
        catch (IOException exception)
        {
            throw new GracefulException($"Bound template resource I/O failed: {exception.Message}", exception, isUserError: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new GracefulException($"Bound template resource access failed: {exception.Message}", exception, isUserError: true);
        }
        catch (NotSupportedException exception)
        {
            throw new GracefulException($"Unsupported bound native metadata: {exception.Message}", exception, isUserError: true);
        }
    }

    public async Task<bool> IsCompatibleAsync(WorkingDirectory directory, TemplateDescriptor item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            Projects.FunctionsProject project = await _application.ResolveProjectAsync(directory, cancellationToken);
            return _matcher.Evaluate(project, item.Requirements, cancellationToken).PassesProjectCapabilityCheck;
        }
        catch (TemplatePreparationException exception)
        {
            throw new GracefulException($"{exception.Failure}: {exception.Message}", exception, isUserError: true);
        }
        catch (IOException exception)
        {
            throw new GracefulException($"Project capability inspection I/O failed: {exception.Message}", exception, isUserError: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new GracefulException($"Project capability inspection access failed: {exception.Message}", exception, isUserError: true);
        }
        catch (FormatException exception)
        {
            throw new GracefulException($"Project capability inspection failed: {exception.Message}", exception, isUserError: true);
        }
    }

    private async Task<TemplateDescriptor> ReadItemCoreAsync(WorkingDirectory directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        PublicTemplatePackageBinding binding = Binding();
        using TemplatePackageContentLease package = await OpenPackageAsync(binding, cancellationToken);
        PackageTemplateSession? session = null;
        Task<TemplateDescriptor> query = ReadCoreAsync();
        try
        {
            return await query;
        }
        finally
        {
            if (session is not null)
            {
                Task cleanup = session.DisposeAsync().AsTask();
                try
                {
                    await cleanup;
                }
                finally
                {
                    if (!query.IsCompletedSuccessfully && !cleanup.IsCompletedSuccessfully)
                    {
                        List<Exception> failures = query.IsCanceled ? [new TaskCanceledException(query)] : [.. query.Exception!.InnerExceptions];
                        failures.AddRange(cleanup.IsCanceled ? [new TaskCanceledException(cleanup)] : cleanup.Exception!.InnerExceptions);
                        throw new AggregateException("Reading bound template metadata and releasing its native state both failed.", failures);
                    }
                }
            }
        }

        async Task<TemplateDescriptor> ReadCoreAsync()
        {
            session = await _adapter.OpenAsync(package, new(directory), cancellationToken);
            PackageTemplateApplication.ValidateProjectDefault(session, binding.ProjectIdentity);
            PackageTemplateCandidate[] matches = [.. session.Candidates.Where(candidate =>
                string.Equals(candidate.Descriptor.Metadata.Identity, binding.ItemIdentity, StringComparison.Ordinal))];
            if (matches.Length != 1 || matches[0].Descriptor.Metadata.Type != TemplateType.Item)
            {
                throw new InvalidTemplateMetadataException($"The bound package must contain exactly one item with identity '{binding.ItemIdentity}'.");
            }

            matches[0].EnsureEligible();
            TemplateCatalogEntry metadata = matches[0].Descriptor.Metadata;
            if (metadata.Parameters.Count != 0 || string.IsNullOrWhiteSpace(metadata.DefaultName))
            {
                throw new InvalidTemplateMetadataException("The qualification HTTP binding requires authored function naming and does not approve additional parameters.");
            }

            return matches[0].Descriptor;
        }
    }

    public FunctionTemplateInfo ProjectItem(TemplateCatalogEntry metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new(Binding().PublicItemAlias, "node", EngineId, metadata.Name,
            $"{metadata.Description} (Preview5 qualification; create-only)", metadata.DefaultName, ["javascript"], new([], false, null));
    }

    internal static void RejectForce(bool force)
    {
        if (force)
        {
            throw new GracefulException(
                "The package-backed Preview5 qualification HTTP route is create-only and does not support --force. Choose a new function name or remove the collision yourself.",
                isUserError: true);
        }
    }

    internal static void ValidateOutput(string? format)
    {
        if (format is not null && !string.Equals(format, "plain", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            throw new GracefulException("Use '--output plain' or '--output json' for the qualification route.", isUserError: true);
        }
    }

    private PublicTemplatePackageBinding Binding()
        => _binding ?? throw new InvalidOperationException("No public package-backed qualification binding is enabled.");

    private Task<TemplatePackageContentLease> OpenPackageAsync(PublicTemplatePackageBinding binding, CancellationToken cancellationToken)
        => _intake.IntakeAsync(binding.Identity, () => _resources.OpenRead(binding.ResourceName), cancellationToken);

    private async Task<PackageTemplateOperation> PrepareAsync(TemplatePreparationRequest request, CancellationToken cancellationToken)
    {
        PublicTemplatePackageBinding binding = Binding();
        TemplatePackageContentLease package = await OpenPackageAsync(binding, cancellationToken);
        return await _application.PrepareAsync(package, request, cancellationToken);
    }
}
