// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand;

/// <summary>
/// Registers stack metadata before failing so tests can distinguish partial services from configured availability.
/// Public for activation across the workload load context.
/// </summary>
public sealed class PartiallyConfiguredStackWorkload : Workload
{
    public override string DisplayName => "Partially configured stack";

    public override string Description => "Fails after contributing stack metadata.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectStack>(new PartialStack());
        throw new InvalidOperationException("Configuration failed after adding stack metadata.");
    }

    private sealed class PartialStack : IProjectStack
    {
        public string Stack => "partial";

        public IReadOnlyList<string> SupportedLanguages => ["Partial"];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
            new Dictionary<string, IReadOnlyList<string>>();
    }
}
