// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Project initializer for .NET (C# and F#) Azure Functions.
/// </summary>
public sealed class DotNetProjectInitializer : IProjectInitializer
{
    public string Stack => "dotnet";

    public IReadOnlyList<string> SupportedLanguages => ["C#", "F#"];

    public IReadOnlyList<Option> GetInitOptions()
    {
        throw new NotImplementedException();
    }

    public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
