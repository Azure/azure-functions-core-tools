// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Stub <see cref="IProjectInitializer"/> for the dotnet workload. Real
/// scaffolding (project files, host.json, etc.) lands in a follow-up; this
/// just proves the workload extension wiring end-to-end.
/// </summary>
internal sealed class DotnetProjectInitializer : IProjectInitializer
{
    public string Stack => "dotnet";

    public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "C#", "F#" };

    public bool CanHandle(string stack) =>
        stack.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        || stack.Equals("csharp", StringComparison.OrdinalIgnoreCase)
        || stack.Equals("fsharp", StringComparison.OrdinalIgnoreCase)
        || stack.Equals("c#", StringComparison.OrdinalIgnoreCase)
        || stack.Equals("f#", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<Option> GetInitOptions() => Array.Empty<Option>();

    public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[dotnet workload stub] would scaffold a dotnet project at '{context.ProjectPath}'");
        if (context.Language is not null)
        {
            Console.WriteLine($"[dotnet workload stub] requested language: {context.Language}");
        }

        return Task.CompletedTask;
    }
}
