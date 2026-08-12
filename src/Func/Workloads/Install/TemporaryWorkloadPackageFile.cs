// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class TemporaryWorkloadPackageFile(string path) : IDisposable
{
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // Temporary package cleanup is best-effort; the operating system reaps abandoned files.
        }
    }
}
