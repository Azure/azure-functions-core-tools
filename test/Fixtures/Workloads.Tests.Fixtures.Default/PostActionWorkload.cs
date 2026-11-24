// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PostActions;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.Default;

/// <summary>
/// Registers a post-action processor from an isolated workload assembly.
/// </summary>
public class PostActionWorkload : Workload
{
    /// <inheritdoc />
    public override string DisplayName => "Post-action fixture";

    /// <inheritdoc />
    public override string Description => "Exercises the workload post-action contract across assembly load contexts.";

    /// <inheritdoc />
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTemplateComponent<Processor>();
    }

    /// <summary>
    /// Registers the processor before failing workload configuration.
    /// </summary>
    public sealed class Failing : PostActionWorkload
    {
        /// <inheritdoc />
        public override void Configure(FunctionsCliBuilder builder)
        {
            base.Configure(builder);
            throw new InvalidOperationException("Fixture failure after registration.");
        }
    }

    private sealed class Processor : IPostActionProcessor
    {
        /// <inheritdoc />
        public Guid Id { get; } = new("A99565D8-BED2-45DB-8322-B048A0D27464");

        /// <inheritdoc />
        public bool CanProcess(PostActionSelectionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return string.Equals(context.Stack, "fixture", StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Language, "Fixture", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(context.Arguments.TryGetValue("fixture", out string? value) && value == "run");
        }
    }
}
