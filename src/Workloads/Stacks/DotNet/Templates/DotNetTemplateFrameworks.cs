// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

internal static class DotNetTemplateFrameworks
{
    public const string Default = "net10.0";

    public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly<string>([Default]);
}
