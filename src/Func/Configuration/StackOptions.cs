// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Configuration;

internal sealed class StackOptions
{
    public const string SectionName = "Stack";

    public string? Runtime { get; set; }

    public string? Language { get; set; }
}
