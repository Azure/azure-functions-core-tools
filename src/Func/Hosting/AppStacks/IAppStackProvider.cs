// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Hosting.AppStacks;

/// <summary>
/// Resolves the function app stack displayed by interactive host views.
/// </summary>
internal interface IAppStackProvider
{
    public ValueTask<string> GetStackNameAsync(WorkingDirectory workingDirectory, CancellationToken cancellationToken);
}
