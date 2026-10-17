// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand;

/// <summary>
/// Contributes stack metadata successfully for boot provenance tests.
/// Public for activation across the workload load context.
/// </summary>
public sealed class ConfiguredStackWorkload : Workload
{
    public override string DisplayName => "Configured stack";

    public override string Description => "Contributes stack metadata during successful configuration.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectStack>(new ConfiguredStack());
    }

    private sealed class ConfiguredStack : IProjectStack
    {
        public string Stack => "configured";

        public IReadOnlyList<string> SupportedLanguages => ["Configured"];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
            new Dictionary<string, IReadOnlyList<string>>();
    }
}
