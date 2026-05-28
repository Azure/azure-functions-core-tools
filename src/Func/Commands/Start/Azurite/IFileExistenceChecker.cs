// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Substitutable wrapper around <see cref="System.IO.File.Exists(string?)"/>
/// so discovery code can be unit-tested without touching the real filesystem.
/// </summary>
internal interface IFileExistenceChecker
{
    public bool Exists(string path);
}
