// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Commands.Init;

public interface IProjectInitializer
{
    public WorkerRuntime Runtime { get; }

    public Task InitializeAsync(ParseResult args, CancellationToken ct);

    public Task WriteDockerfileAsync(ParseResult args, CancellationToken ct);

    public Task PostInstallAsync(ParseResult args, CancellationToken ct);
}
