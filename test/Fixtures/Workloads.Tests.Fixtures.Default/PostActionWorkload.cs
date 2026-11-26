// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PostActions;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.Default;

public class PostActionWorkload : Workload
{
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTemplateComponent<Processor>();
    }

    public sealed class Failing : PostActionWorkload
    {
        public override void Configure(FunctionsCliBuilder builder)
        {
            base.Configure(builder);
            throw new InvalidOperationException("Fixture failure after registration.");
        }
    }

    private sealed class Processor : IPostActionProcessor
    {
        public Guid Id { get; } = new("A99565D8-BED2-45DB-8322-B048A0D27464");

        public bool CanProcess(PostActionSelectionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return string.Equals(context.Stack, "fixture", StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Language, "Fixture", StringComparison.OrdinalIgnoreCase);
        }

        public Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(context.Arguments.TryGetValue("fixture", out string? value) && value == "run");
        }
    }
}
