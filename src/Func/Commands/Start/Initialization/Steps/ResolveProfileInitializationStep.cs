// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Profiles;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the active start profile.
/// </summary>
internal sealed class ResolveProfileInitializationStep(IProfileResolver resolver) : DemoInitializationStep
{
    public const string StepId = "resolve_profile";

    private readonly IProfileResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public override string Id => StepId;

    public override string Title => "Resolve profile";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ProfileResolution resolution;
        try
        {
            var profileResolutionContext = new ProfileResolutionContext(
                context.Options.WorkingDirectory.Info,
                context.Options.RequestedProfileName,
                context.CanPrompt);

            resolution = await _resolver.ResolveAsync(profileResolutionContext, cancellationToken);
        }
        catch (ProfileConfigurationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true, verboseMessage: ex.ToString());
        }

        context.State.ProfileResolution = resolution;
        if (resolution is ProfileResolution.Resolved resolved)
        {
            context.State.ResolvedProfile = resolved.Profile;
            context.State.ProfileName = resolved.Profile.Name;

            string message = $"{resolved.Profile.Name} ({resolved.Profile.Source.KindDisplayName})";
            return StartInitializationStepResult.Completed(message);
        }

        context.State.ProfileName = "none";
        return StartInitializationStepResult.Completed("None (no profile applied)");
    }
}
