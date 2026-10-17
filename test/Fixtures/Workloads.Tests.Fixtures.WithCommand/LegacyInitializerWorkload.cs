// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand;

/// <summary>
/// Exposes only the older initializer contract. Public for activation across the workload load context.
/// </summary>
public sealed class LegacyInitializerWorkload : Workload
{
    public override string DisplayName => "Legacy initializer fixture";

    public override string Description => "Contributes configuration metadata but must never scaffold through native entry.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer>(new LegacyInitializer());
    }

    private sealed class LegacyInitializer : IProjectInitializer
    {
        public string Stack => "legacy";

        public IReadOnlyList<string> SupportedLanguages => ["Legacy", "Other"];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
            new Dictionary<string, IReadOnlyList<string>>();

        public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry) => [];

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Legacy scaffolding must not be invoked by public native entry.");
    }
}
