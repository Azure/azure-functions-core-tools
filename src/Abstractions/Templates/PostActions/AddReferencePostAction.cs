// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Processes the standard add-reference post action using workload-specific target discovery and reference operations.
/// </summary>
/// <remarks>
/// Register reference types and handlers with <see cref="RegisterReferenceHandlers"/> during construction.
/// Override <see cref="TryGetReference"/> to customize reference creation, or <see cref="TryFindTargets"/>
/// to discover targets when <c>targetFiles</c> matches nothing.
/// </remarks>
public abstract class AddReferencePostAction<TReference> : IPostActionProcessor
    where TReference : PostActionReference
{
    private readonly Dictionary<string, ReferenceHandler> _referenceHandlers = [with(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Adds a reference to one target.
    /// </summary>
    /// <param name="context">The post-action context.</param>
    /// <param name="targetPath">The absolute path of the target receiving the reference.</param>
    /// <param name="reference">The requested reference, without workload-specific path resolution.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the reference was added; otherwise, false.</returns>
    protected delegate Task<bool> ReferenceHandler(
        PostActionContext context, string targetPath, TReference reference, CancellationToken cancellationToken);

    /// <inheritdoc />
    public Guid Id => new("B17581D1-C5C9-4489-8F0A-004BE667B814");

    /// <inheritdoc />
    public abstract bool CanProcess(PostActionSelectionContext context);

    /// <inheritdoc />
    public async Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetReference(context, out TReference? reference))
        {
            return false;
        }

        IReadOnlyList<string> targets = context.GetConfiguredFiles("targetFiles");
        if (targets.Count == 0 && !TryFindTargets(context, out targets))
        {
            return false;
        }

        if (targets.Count == 0)
        {
            context.Reporter.WriteLog(
                "Could not find a target to add the reference to. Specify the 'targetFiles' argument.",
                FunctionsReportSeverity.Error);
            return false;
        }

        if (!_referenceHandlers.TryGetValue(reference.ReferenceType, out ReferenceHandler? handler))
        {
            context.Reporter.WriteLog(
                $"Reference type '{reference.ReferenceType}' is not supported for this template.",
                FunctionsReportSeverity.Error);
            return false;
        }

        foreach (string target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = await handler(context, target, reference, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!succeeded)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds targets when the <c>targetFiles</c> argument is absent or matches no generated files.
    /// </summary>
    /// <param name="context">The post-action context.</param>
    /// <param name="targets">The absolute target paths; empty when none were found.</param>
    /// <returns>False when discovery failed and the error was reported; otherwise, true.</returns>
    protected virtual bool TryFindTargets(PostActionContext context, out IReadOnlyList<string> targets)
    {
        targets = [];
        return true;
    }

    /// <summary>
    /// Creates a workload-specific reference from the post-action arguments.
    /// </summary>
    /// <param name="context">The post-action context, including all arguments.</param>
    /// <param name="referenceType">The required reference type.</param>
    /// <param name="referenceValue">The required reference value.</param>
    /// <param name="reference">The parsed reference, or null when creation fails.</param>
    /// <returns>True when the reference was created; otherwise, false.</returns>
    protected virtual bool TryGetReference(
        PostActionContext context,
        string referenceType,
        string referenceValue,
        [NotNullWhen(true)] out TReference? reference)
    {
        ArgumentNullException.ThrowIfNull(context);

        reference = null;
        var arguments = context.Arguments.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        arguments["reference"] = referenceValue;
        arguments["referenceType"] = referenceType;
        if (arguments.TryGetValue("version", out string? version) && string.IsNullOrWhiteSpace(version))
        {
            arguments.Remove("version");
        }

        try
        {
            reference = JsonSerializer.Deserialize<TReference>(
                JsonSerializer.Serialize(arguments), JsonSerializerOptions.Web);
        }
        catch (JsonException ex)
        {
            context.Reporter.WriteLog(
                $"The add-reference post action is misconfigured: its arguments could not be read for "
                + $"reference type '{referenceType}'. {ex.Message}",
                FunctionsReportSeverity.Error);
            return false;
        }

        if (reference is not null)
        {
            return true;
        }

        context.Reporter.WriteLog(
            $"The add-reference post action is misconfigured: its arguments could not be read for "
            + $"reference type '{referenceType}'.",
            FunctionsReportSeverity.Error);
        return false;
    }

    private bool TryGetReference(PostActionContext context, [NotNullWhen(true)] out TReference? reference)
    {
        reference = null;

        // avoid short-circuiting so both errors are reported (if both are an issue).
        bool hasRequiredArguments = TryGetRequiredArgument(context, "reference", out string? referenceValue);
        hasRequiredArguments &= TryGetRequiredArgument(context, "referenceType", out string? referenceType);
        if (!hasRequiredArguments)
        {
            return false;
        }

        if (referenceValue is null || referenceType is null)
        {
            return false;
        }

        return TryGetReference(context, referenceType.Trim(), referenceValue, out reference);
    }

    /// <summary>
    /// Registers the workload's reference handlers during construction, matching reference types case-insensitively.
    /// </summary>
    /// <param name="handlers">The supported reference types and their handlers.</param>
    /// <exception cref="ArgumentNullException">The list or a handler is null.</exception>
    /// <exception cref="ArgumentException">A reference type is blank or already registered.</exception>
    protected void RegisterReferenceHandlers(params (string ReferenceType, ReferenceHandler Handler)[] handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        foreach ((string referenceType, ReferenceHandler handler) in handlers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(referenceType);
            ArgumentNullException.ThrowIfNull(handler);
            _referenceHandlers.Add(referenceType.Trim(), handler);
        }
    }

    private static bool TryGetRequiredArgument(
        PostActionContext context, string name, [NotNullWhen(true)] out string? value)
    {
        if (context.Arguments.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        context.Reporter.WriteLog(
            $"The add-reference post action is misconfigured: the '{name}' argument is required.",
            FunctionsReportSeverity.Error);
        value = null;
        return false;
    }
}
