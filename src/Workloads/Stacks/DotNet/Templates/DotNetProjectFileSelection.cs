// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

/// <summary>
/// Applies the source-project file filter shared by workload detection and controlled template preparation.
/// </summary>
internal static class DotNetProjectFileSelection
{
    public static IReadOnlyList<string> Candidates(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return Array.AsReadOnly<string>([.. files.Where(path => Path.GetExtension(path) is ".csproj" or ".fsproj")]);
    }
}
