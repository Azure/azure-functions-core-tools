// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandContextResolver
{
    public Task<NewCommandResolutionResult> ResolveAsync(NewInvocation invocation, CancellationToken cancellationToken);
}

internal sealed class NewCommandContextResolver(
    IInteractionService interaction,
    IFunctionsProjectResolver projectResolver,
    IProfileResolver profileResolver,
    IOptionsMonitor<StackOptions> stackOptions,
    IEnumerable<IProjectInitializer> projectInitializers,
    IInstalledTemplatesWorkloads installedTemplatesWorkloads,
    IHostJsonBundleSectionReader hostJsonReader) : INewCommandContextResolver
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IFunctionsProjectResolver _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
    private readonly IProfileResolver _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
    private readonly IOptionsMonitor<StackOptions> _stackOptions = stackOptions ?? throw new ArgumentNullException(nameof(stackOptions));
    private readonly IReadOnlyDictionary<string, IProjectInitializer> _projectInitializersByStack = BuildProjectInitializersByStack(projectInitializers);
    private readonly IInstalledTemplatesWorkloads _installedTemplatesWorkloads = installedTemplatesWorkloads ?? throw new ArgumentNullException(nameof(installedTemplatesWorkloads));
    private readonly IHostJsonBundleSectionReader _hostJsonReader = hostJsonReader ?? throw new ArgumentNullException(nameof(hostJsonReader));

    public async Task<NewCommandResolutionResult> ResolveAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await _profileResolver.ResolveAsync(
            new ProfileResolutionContext(
                invocation.WorkingDirectory.Info,
                RequestedProfileName: null,
                CanPrompt: _interaction.IsInteractive),
            cancellationToken);

        ProjectResolutionResult projectResult = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(invocation.WorkingDirectory),
            cancellationToken);

        if (projectResult is not ProjectResolutionResult.Resolved resolved)
        {
            return NewCommandResolutionResult.Fail(new NewCommandResolutionFailure(NewCommandResolutionFailureKind.ProjectRequired));
        }

        string stack = resolved.Project.StackName;
        InstalledTemplatesWorkload? workload;
        string? bundleId = null;
        BundleChannel channel = BundleChannel.Unknown;
        bool usedStableFallback = false;

        if (string.Equals(stack, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<InstalledTemplatesWorkload> allRows =
                await _installedTemplatesWorkloads.ListInstalledAsync(stack, cancellationToken);
            workload = allRows
                .OrderByDescending(row => row.PackageVersion, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        else
        {
            HostJsonBundleSection? section = await _hostJsonReader.ReadAsync(invocation.WorkingDirectory.Info, cancellationToken);
            if (section is null || string.IsNullOrWhiteSpace(section.Id))
            {
                return NewCommandResolutionResult.Fail(
                    new NewCommandResolutionFailure(NewCommandResolutionFailureKind.HostJsonBundleMissing));
            }

            bundleId = section.Id;
            if (!BundleHelpers.TryGetBundleChannel(bundleId, out channel))
            {
                return NewCommandResolutionResult.Fail(new NewCommandResolutionFailure(
                    NewCommandResolutionFailureKind.UnrecognisedBundleId,
                    BundleId: bundleId));
            }

            IReadOnlyList<InstalledTemplatesWorkload> allRows =
                await _installedTemplatesWorkloads.ListInstalledAsync(stack, cancellationToken);
            workload = TemplatesChannelMapper.PickChannelMatched(allRows, channel);

            if (workload is null && channel != BundleChannel.Stable)
            {
                workload = TemplatesChannelMapper.PickChannelMatched(allRows, BundleChannel.Stable);
                usedStableFallback = workload is not null;
            }
        }

        if (workload is null)
        {
            if (channel is BundleChannel.Unknown)
            {
                return NewCommandResolutionResult.Fail(new NewCommandResolutionFailure(
                    NewCommandResolutionFailureKind.NoTemplatesWorkloadInstalled,
                    Stack: stack));
            }

            return NewCommandResolutionResult.Fail(new NewCommandResolutionFailure(
                NewCommandResolutionFailureKind.NoTemplatesWorkloadForChannel,
                Stack: stack,
                BundleId: bundleId,
                Channel: channel));
        }

        string projectDirectory = Path.GetFullPath(invocation.WorkingDirectory.Info.FullName);
        StackOptions stackOptionsBound = _stackOptions.Get(projectDirectory);
        string? language = ResolveLanguage(stack, stackOptionsBound);
        if (language is null)
        {
            return NewCommandResolutionResult.Fail(new NewCommandResolutionFailure(
                NewCommandResolutionFailureKind.MissingLanguage,
                Stack: stack,
                ProjectPath: projectDirectory));
        }

        return NewCommandResolutionResult.Succeed(new NewCommandResolvedContext(
            invocation.WorkingDirectory,
            stack,
            language,
            workload,
            bundleId,
            channel,
            usedStableFallback));
    }

    private static IReadOnlyDictionary<string, IProjectInitializer> BuildProjectInitializersByStack(
        IEnumerable<IProjectInitializer> projectInitializers)
    {
        ArgumentNullException.ThrowIfNull(projectInitializers);

        return projectInitializers
            .Where(initializer => !string.IsNullOrWhiteSpace(initializer.Stack))
            .GroupBy(initializer => initializer.Stack.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private string? ResolveLanguage(string stack, StackOptions stackOptions)
    {
        if (!string.IsNullOrWhiteSpace(stackOptions.Language))
        {
            return stackOptions.Language.Trim();
        }

        if (_projectInitializersByStack.TryGetValue(stack, out IProjectInitializer? initializer)
            && initializer.SupportedLanguages.Count == 1)
        {
            return initializer.SupportedLanguages[0];
        }

        return null;
    }
}

internal sealed record NewCommandResolvedContext(
    WorkingDirectory WorkingDirectory,
    string Stack,
    string Language,
    InstalledTemplatesWorkload Workload,
    string? BundleId,
    BundleChannel Channel,
    bool UsedStableFallback);

internal enum NewCommandResolutionFailureKind
{
    ProjectRequired,
    HostJsonBundleMissing,
    UnrecognisedBundleId,
    NoTemplatesWorkloadInstalled,
    NoTemplatesWorkloadForChannel,
    MissingLanguage,
}

internal sealed record NewCommandResolutionFailure(
    NewCommandResolutionFailureKind Kind,
    string? Stack = null,
    string? ProjectPath = null,
    string? BundleId = null,
    BundleChannel Channel = BundleChannel.Unknown);

internal readonly struct NewCommandResolutionResult
{
    private NewCommandResolutionResult(NewCommandResolvedContext? context, NewCommandResolutionFailure? failure)
    {
        Context = context;
        Failure = failure;
    }

    public NewCommandResolvedContext? Context { get; }

    public NewCommandResolutionFailure? Failure { get; }

    public static NewCommandResolutionResult Succeed(NewCommandResolvedContext context) => new(context, null);

    public static NewCommandResolutionResult Fail(NewCommandResolutionFailure failure) => new(null, failure);
}