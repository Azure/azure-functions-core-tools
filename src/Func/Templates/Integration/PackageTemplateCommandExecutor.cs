// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Executes retained preparation and publication with one truthful completion report, including exceptional completion.
/// </summary>
internal sealed class PackageTemplateCommandExecutor(IInteractionService interaction)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public async Task<int> ExecuteAsync(
        string command, TemplatePreparationRequest request, bool json, bool preview,
        Func<CancellationToken, Task<PackageTemplateOperation>> prepare, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(prepare);
        PackageTemplateOperation? operation = null;
        PreparedOperationPlan? plan = null;
        Task execution = ExecuteCoreAsync();
        try
        {
            await execution;
        }
        catch (TemplatePreparationException exception)
        {
            IEnumerable<string> diagnostics = exception.Diagnostics
                .Concat(exception.Selection?.Diagnostics ?? [])
                .Concat(exception.Compatibility?.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}") ?? []);
            throw new GracefulException(
                $"{exception.Failure}: {exception.Message}\n{string.Join("\n", diagnostics)}", exception, isUserError: true);
        }
        catch (InvalidTemplatePackageException exception)
        {
            throw new GracefulException($"Package intake failed: {exception.Message}", exception, isUserError: true);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            throw new GracefulException($"Native metadata validation failed: {exception.Message}", exception, isUserError: true);
        }
        catch (NotSupportedException exception)
        {
            throw new GracefulException($"Unsupported native declaration: {exception.Message}", exception, isUserError: true);
        }
        finally
        {
            if (operation is not null)
            {
                Task cleanup = operation.DisposeAsync().AsTask();
                try
                {
                    await cleanup;
                }
                finally
                {
                    if ((!execution.IsCompletedSuccessfully || !cleanup.IsCompletedSuccessfully)
                        && operation.Completion.CompletedEffects.Count != 0)
                    {
                        RenderCompletion(successful: false);
                    }

                    if (!cleanup.IsCompletedSuccessfully && !execution.IsCompletedSuccessfully)
                    {
                        List<Exception> failures = execution.IsCanceled ? [new TaskCanceledException(execution)] : [.. execution.Exception!.InnerExceptions];
                        failures.AddRange(cleanup.IsCanceled ? [new TaskCanceledException(cleanup)] : cleanup.Exception!.InnerExceptions);
                        throw new AggregateException("Template execution and cleanup both failed.", failures);
                    }
                }
            }
        }

        bool succeeded = preview || operation!.Completion.Status == PreparedCommitStatus.Succeeded;
        RenderCompletion(succeeded);
        return succeeded ? 0 : 1;

        void RenderCompletion(bool successful)
        {
            PreparedOperationCompletion completion = operation!.Completion;
            if (json)
            {
                _interaction.WriteJson(new
                {
                    command,
                    preview,
                    operation.PackageSha256,
                    template = request.Selection.Reference,
                    completion.Status,
                    completion.CompletedEffects,
                    completion.CleanupStatus,
                    projectRoot = plan!.Configuration is { ProjectRootRelativePath: not "." } configuration
                        ? Path.Combine(plan.TargetDirectory, configuration.ProjectRootRelativePath.Replace('/', Path.DirectorySeparatorChar))
                        : plan.TargetDirectory,
                    compatibility = plan.Compatibility?.Compatibility.ToString(),
                    requirements = plan.Compatibility?.Requirements.CapabilityIds,
                });
            }
            else if (!successful)
            {
                _interaction.WriteError(
                    $"Template '{request.Selection.Reference}' did not complete successfully. Publication status: {completion.Status}; cleanup status: {completion.CleanupStatus}.");
                foreach (PreparedEffect effect in completion.CompletedEffects)
                {
                    _interaction.WriteLine($"{effect.Kind}: {effect.Path}");
                }
            }
            else
            {
                _interaction.WriteSuccess(preview
                    ? $"Prepared '{request.Selection.Reference}' without publishing target files."
                    : $"Created '{request.Selection.Reference}' using retained package output.");
            }
        }

        async Task ExecuteCoreAsync()
        {
            try
            {
                operation = await prepare(cancellationToken);
            }
            catch (IOException exception)
            {
                throw new GracefulException($"Package template preparation I/O failed: {exception.Message}", exception, isUserError: true);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new GracefulException($"Package template preparation access failed: {exception.Message}", exception, isUserError: true);
            }
            catch (FormatException exception)
            {
                throw new GracefulException($"Project capability inspection failed: {exception.Message}", exception, isUserError: true);
            }

            plan = operation.Preview();
            if (!preview)
            {
                try
                {
                    await operation.CommitAsync(cancellationToken);
                }
                catch (IOException exception)
                {
                    throw new GracefulException(
                        $"Create-only publication failed: {exception.Message} Status: {operation.Completion.Status}; completed effects: {operation.Completion.CompletedEffects.Count}.",
                        exception, isUserError: true);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new GracefulException($"Create-only publication access failed: {exception.Message}", exception, isUserError: true);
                }
            }
        }
    }
}
