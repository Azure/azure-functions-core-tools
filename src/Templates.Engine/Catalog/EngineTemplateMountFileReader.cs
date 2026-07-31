// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Utils;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Reads mount-relative template files through the engine's mount-point
/// abstraction, so the same code serves folder-installed and nupkg-installed
/// templates.
/// </summary>
internal sealed class EngineTemplateMountFileReader(IFuncTemplateEngineSession session) : IFuncTemplateMountFileReader
{
    private readonly IFuncTemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public string? TryReadFile(ITemplateInfo template, string? mountRelativePath)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (string.IsNullOrEmpty(mountRelativePath))
        {
            return null;
        }

        if (!_session.Settings.TryGetMountPoint(template.MountPointUri, out IMountPoint? mount) || mount is null)
        {
            return null;
        }

        using (mount)
        {
            IFile? file = mount.FileInfo(mountRelativePath);
            if (file is null || !file.Exists)
            {
                return null;
            }

            using Stream stream = file.OpenRead();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
