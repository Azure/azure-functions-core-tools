// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Root 'func' command definition. Separate from BaseCommand because RootCommand
/// has distinct behavior (no-args → help, global options, version display).
/// </summary>
internal class FuncRootCommand : RootCommand
{
    public Option<bool> VerboseOption { get; } = new("--verbose", "-v")
    {
        Description = "Enable verbose output",
        Recursive = true
    };

    public FuncRootCommand()
        : base("Azure Functions CLI — Create, develop, test, and deploy Azure Functions locally.")
    {
        Options.Add(VerboseOption);
    }
}
