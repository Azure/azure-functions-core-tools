// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Project initializer for .NET (C# and F#) Azure Functions.
/// </summary>
internal sealed class DotNetProjectInitializer : IProjectInitializer
{
    public string Stack => "dotnet";

    public IReadOnlyList<string> SupportedLanguages => ["C#", "F#", "csharp", "fsharp"];

    public IReadOnlyList<Option> GetInitOptions() => [];

    public Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            ".NET project initialization is not implemented yet.");
    }
}
