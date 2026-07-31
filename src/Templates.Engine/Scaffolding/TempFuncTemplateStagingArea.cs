// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Temp-directory implementation of <see cref="IFuncTemplateStagingArea"/>.
/// </summary>
internal sealed class TempFuncTemplateStagingArea : IFuncTemplateStagingArea
{
    /// <inheritdoc />
    public string Create() => Directory.CreateTempSubdirectory("func-template-").FullName;

    /// <inheritdoc />
    public void Cleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the staging directory lives under the temp path, so
            // leaking it on a transient I/O error is harmless.
        }
    }
}
