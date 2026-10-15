// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tests.Templates.Engine;

internal sealed class SymbolicLinkFactAttribute : FactAttribute
{
    public SymbolicLinkFactAttribute()
    {
        string root = Path.Combine(Path.GetTempPath(), "func-symlink-probe-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (IOException exception) when (OperatingSystem.IsWindows() && (exception.HResult & 0xFFFF) == 1314)
        {
            Skip = "Creating symbolic links requires Windows Developer Mode or the symbolic-link privilege.";
        }
        catch (UnauthorizedAccessException)
        {
            Skip = "Creating symbolic links is not permitted.";
        }
        catch (PlatformNotSupportedException)
        {
            Skip = "Symbolic links are not supported on this platform.";
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
