// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Reads a single MSBuild property from the project file in a directory,
/// behind an interface so the <c>msbuild:</c> bind source stays unit-testable
/// without a real project on disk.
/// </summary>
internal interface IProjectFilePropertyReader
{
    /// <summary>
    /// Returns the value of <paramref name="propertyName"/> from the first
    /// project file found in <paramref name="projectDirectory"/>, or
    /// <c>null</c> when the directory, file, or property is absent.
    /// </summary>
    public string? TryReadProperty(string projectDirectory, string propertyName);
}
