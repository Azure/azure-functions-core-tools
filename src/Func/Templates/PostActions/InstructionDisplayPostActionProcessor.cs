// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Ported from dotnet/sdk (MIT): src/Cli/Microsoft.TemplateEngine.Cli/PostActionProcessors/InstructionDisplayPostActionProcessor.cs
// at c4d14e7b72e0dbe973154c105fbba459126e2bf4.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Functions.Cli.Console;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Displays a post action's description, manual instructions, and optional command for the user to run.
/// </summary>
/// <remarks>
/// The action always succeeds, so the dispatcher's failure fallback never prints the instructions a second time.
/// </remarks>
internal sealed class InstructionDisplayPostActionProcessor(IInteractionService interaction) : ICorePostActionProcessor
{
    internal static readonly Guid ActionId = new("AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C");

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <inheritdoc />
    public Guid Id => ActionId;

    /// <inheritdoc />
    public Task<bool> ProcessAsync(EnginePostActionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IPostAction action = context.Action;
        _interaction.WriteLine($"Description: {action.Description}");
        _interaction.WriteLine($"Manual instructions: {action.ManualInstructions}");
        if (action.Args is not null && action.Args.TryGetValue("executable", out string? executable))
        {
            action.Args.TryGetValue("args", out string? args);
            _interaction.WriteLine(line => line.Plain("Actual command: ").Command($"{executable} {args}"));
        }

        return Task.FromResult(true);
    }
}
