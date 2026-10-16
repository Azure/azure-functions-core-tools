// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Python;

internal enum PythonTemplateApplicationMode
{
    CreateNewApp,
    AppendToFile,
}

internal sealed record PythonTemplateApplication(
    PythonTemplateApplicationMode Mode, string TargetPath, string Content, string? ExpectedTargetSha256);

/// <summary>
/// Chooses the legacy CLI's default app job using approved target state and an explicit native source-expansion callback.
/// </summary>
internal sealed class PythonTemplateApplicationPlanner
{
    /// <summary>
    /// Selects the original job using the declared create target's presence and an independently approved append target digest.
    /// </summary>
    /// <exception cref="ArgumentException">The project path or approved digest is invalid.</exception>
    /// <exception cref="ArgumentNullException">The native expansion callback is null.</exception>
    /// <exception cref="InvalidOperationException">The append target is unapproved or native expansion returns an empty payload.</exception>
    public async Task<PythonTemplateApplication> PlanAsync(string absoluteProjectDirectory, string createTargetFileName,
        bool createTargetExists, string? expectedAppendTargetSha256,
        Func<PythonTemplateApplicationMode, CancellationToken, Task<string>> expandNativeSource, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteProjectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(createTargetFileName);
        ArgumentNullException.ThrowIfNull(expandNativeSource);
        if (!Path.IsPathFullyQualified(absoluteProjectDirectory))
        {
            throw new ArgumentException("The Python project directory must be absolute.", nameof(absoluteProjectDirectory));
        }

        if (createTargetFileName is not "function_app.py" and not "function_app.py.py")
        {
            throw new ArgumentException("The selected Python create target is not a supported legacy app filename.", nameof(createTargetFileName));
        }

        if (expectedAppendTargetSha256 is not null
            && (expectedAppendTargetSha256.Length != 64 || expectedAppendTargetSha256.Any(character => !char.IsAsciiHexDigit(character))))
        {
            throw new ArgumentException("An existing Python app requires its approved SHA-256 digest.", nameof(expectedAppendTargetSha256));
        }

        if (createTargetExists && expectedAppendTargetSha256 is null)
        {
            throw new InvalidOperationException("The Python append target must exist and be approved before expanding its function body.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        PythonTemplateApplicationMode mode = createTargetExists ? PythonTemplateApplicationMode.AppendToFile : PythonTemplateApplicationMode.CreateNewApp;
        string content = await expandNativeSource(mode, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Native Python template expansion did not produce content. No app should be changed.");
        }

        return new(mode, Path.Combine(absoluteProjectDirectory, createTargetExists ? "function_app.py" : createTargetFileName),
            content, createTargetExists ? expectedAppendTargetSha256 : null);
    }
}
