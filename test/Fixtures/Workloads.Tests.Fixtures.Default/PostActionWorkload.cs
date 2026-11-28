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

    private sealed class Processor : RestorePostAction
    {
        public override bool CanProcess(PostActionSelectionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return string.Equals(context.Stack, "fixture", StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Language, "Fixture", StringComparison.OrdinalIgnoreCase);
        }

        protected override Task<bool> RestoreAsync(PostActionContext context, string targetPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.Arguments.TryGetValue("fixture", out string? value)
                || value != "run"
                || !context.Arguments.TryGetValue("expectedTarget", out string? target))
            {
                return Task.FromResult(false);
            }

            string expectedPath = Path.GetFullPath(target, Path.GetFullPath(context.OutputBasePath));
            return Task.FromResult(string.Equals(targetPath, expectedPath, StringComparison.Ordinal));
        }
    }
}
