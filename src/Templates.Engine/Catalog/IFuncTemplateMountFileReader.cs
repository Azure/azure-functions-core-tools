// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Reads a file that lives inside a template's mount point (the installed
/// package or source folder) — e.g. <c>func.host.json</c> or the template's
/// own <c>template.json</c> — without materialising it to disk.
/// </summary>
internal interface IFuncTemplateMountFileReader
{
    /// <summary>
    /// Returns the text content of the mount-relative file, or <c>null</c>
    /// when the path is empty, the mount can't be opened, or the file is
    /// absent.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    public string? TryReadFile(ITemplateInfo template, string? mountRelativePath);
}
