// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

/// <summary>
/// A label + description pair rendered as an aligned row in a definition list
/// (used for command listings, option listings, etc.).
/// </summary>
public readonly record struct DefinitionItem(string Label, string Description);
