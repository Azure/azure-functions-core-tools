// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

/// <summary>
/// Per-test temp directory created under the OS temp folder and recursively
/// deleted on dispose. Use as a stand-in for <c>&lt;funcHome&gt;</c>.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "func-azurite-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best effort: leaving a stray temp folder is preferable to failing
            // tests on teardown.
        }
    }
}
