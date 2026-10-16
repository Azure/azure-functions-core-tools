// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

/// <summary>
/// Resolves the full path to the <c>dotnet</c> muxer executable.
/// </summary>
internal interface IDotnetPathResolver
{
    /// <summary>
    /// Returns the absolute path to the <c>dotnet</c> executable.
    /// </summary>
    /// <exception cref="Azure.Functions.Cli.Common.GracefulException">
    /// The dotnet host could not be located.
    /// </exception>
    public string Resolve();
}
