// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Win32.SafeHandles;

#if FUNC_PYTHON_TEMPLATE_HELPERS
namespace Azure.Functions.Cli.Templates.Python.Shared;
#else
namespace Azure.Functions.Cli.Workloads.Python;
#endif

/// <summary>
/// Applies a native-expanded Python function body to the exact app file approved by a staged item workflow.
/// </summary>
internal interface IPythonTemplateAppendExecutor
{
    /// <summary>
    /// Verifies the existing app's bytes and appends the snippet without replacing its content.
    /// </summary>
    /// <exception cref="ArgumentException">A path, snippet or digest is invalid.</exception>
    /// <exception cref="FileNotFoundException">The approved app file no longer exists.</exception>
    /// <exception cref="DirectoryNotFoundException">The app's parent directory no longer exists.</exception>
    /// <exception cref="InvalidOperationException">The approved app changed or a statically known name collides.</exception>
    /// <exception cref="IOException">The file cannot be opened or appended; a failed write can leave a partial append.</exception>
    /// <exception cref="UnauthorizedAccessException">The app file cannot be accessed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupted the operation; cancellation during writing can leave a partial append.</exception>
    public Task AppendAsync(string absoluteTargetPath, string generatedSnippet, string expectedTargetSha256, CancellationToken cancellationToken);

    /// <summary>
    /// Validates the actual held selected-file handle before appending an owned snippet.
    /// </summary>
    /// <exception cref="ArgumentNullException">The held-target validator is null.</exception>
    /// <exception cref="InvalidOperationException">The selected target or its identity no longer matches its approved state.</exception>
    /// <exception cref="IOException">Identity validation or file access fails; a failed write can leave a partial append.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupted the operation.</exception>
    public Task AppendSelectedAsync(string absoluteTargetPath, string generatedSnippet, string expectedTargetSha256,
        Action<SafeFileHandle> validateHeldTarget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validateHeldTarget);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("This Python append executor does not support held-target identity validation. Nothing was appended.");
    }
}
