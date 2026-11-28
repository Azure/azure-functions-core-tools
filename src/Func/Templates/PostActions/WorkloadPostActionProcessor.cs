// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Routes one action ID to the unique workload processor that accepts the selected template.
/// </summary>
/// <param name="id">The action identifier shared by the workload processors.</param>
/// <param name="registrations">The workload processors eligible for selection.</param>
/// <param name="interaction">The service used to report selection failures.</param>
internal sealed class WorkloadPostActionProcessor(
    Guid id,
    IReadOnlyList<(RuntimeWorkloadInfo Workload, IPostActionProcessor Processor)> registrations,
    IInteractionService interaction) : ICorePostActionProcessor
{
    private readonly IReadOnlyList<(RuntimeWorkloadInfo Workload, IPostActionProcessor Processor)> _registrations = registrations
        ?? throw new ArgumentNullException(nameof(registrations));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <inheritdoc />
    public Guid Id { get; } = id;

    /// <inheritdoc />
    public Task<bool> ProcessAsync(EnginePostActionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        context.Template.TagsCollection.TryGetValue("stack", out string? stack);
        context.Template.TagsCollection.TryGetValue("language", out string? language);
        PostActionSelectionContext selection = new(stack ?? string.Empty, language ?? string.Empty);
        (RuntimeWorkloadInfo Workload, IPostActionProcessor Processor)[] matches = [.. _registrations.Where(registration =>
            registration.Processor.CanProcess(selection))];
        if (matches.Length != 1)
        {
            string reason = matches.Length == 0
                ? "No workload processor matches"
                : $"Multiple workload processors ({string.Join(", ", matches.Select(match =>
                    $"{match.Workload.PackageId}: {match.Processor.GetType().FullName}"))}) match";
            _interaction.WriteError(
                $"{reason} post action '{Id}' for template stack '{selection.Stack}' and language '{selection.Language}'.");
            return Task.FromResult(false);
        }

        IPostAction action = context.Action;
        IPostActionFileMatcher? fileMatcher = context.CreationEffects is ICreationEffects2 effects
            ? new PostActionFileMatcher(effects.FileChanges)
            : null;
        // TODO: Implement a reporter that displays workload post-action output.
        PostActionContext snapshot = new(
            action.ActionId,
            action.Args ?? new Dictionary<string, string>(),
            action.Description,
            action.ManualInstructions,
            action.ContinueOnError,
            context.OutputBasePath,
            context.CreationResult.PrimaryOutputs.Select(output => output.Path.Replace(Path.DirectorySeparatorChar, '/')).ToArray(),
            context.CreationEffects.FileChanges?.Select(change =>
            {
                string targetRelativePath = change.TargetRelativePath.Replace(Path.DirectorySeparatorChar, '/');
                if (targetRelativePath.StartsWith("./", StringComparison.Ordinal))
                {
                    targetRelativePath = targetRelativePath[2..];
                }

                return new PostActionFileChange(
                    targetRelativePath,
                    change.ChangeKind switch
                    {
                        ChangeKind.Create => PostActionFileChangeKind.Create,
                        ChangeKind.Delete => PostActionFileChangeKind.Delete,
                        ChangeKind.Overwrite => PostActionFileChangeKind.Overwrite,
                        ChangeKind.Change => PostActionFileChangeKind.Change,
                        _ => throw new InvalidOperationException($"Unknown template file change kind '{change.ChangeKind}'."),
                    });
            }).ToArray() ?? [],
            fileMatcher: fileMatcher);
        return matches[0].Processor.ProcessAsync(snapshot, cancellationToken);
    }
}
