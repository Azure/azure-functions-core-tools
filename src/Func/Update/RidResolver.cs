// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Default <see cref="IRidResolver"/>. The RID table mirrors the matrix encoded
/// in <c>install.sh</c> / <c>install.ps1</c>; keep them in sync when adding a
/// new shipped platform.
/// </summary>
internal sealed class RidResolver : IRidResolver
{
    private const string WindowsZipExtension = ".zip";
    private const string UnixArchiveExtension = ".tar.gz";

    private static readonly string[] _supportedRids =
    [
        "win-x64",
        "win-arm64",
        "osx-x64",
        "osx-arm64",
        "linux-x64",
        "linux-arm64",
    ];

    public string GetCurrentRid() => Resolve(GetCurrentOs(), RuntimeInformation.ProcessArchitecture);

    public ReleaseAsset? SelectAsset(Release release, string rid)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrEmpty(rid);

        ReleaseAsset[] matches = [.. release.Assets.Where(a => a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))];

        if (matches.Length == 0)
        {
            return null;
        }

        // Releases sometimes ship both a .zip and a .tar.gz for the same RID.
        // Pick the one the install scripts would download for that platform.
        string preferredExt = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
            ? WindowsZipExtension
            : UnixArchiveExtension;

        ReleaseAsset? preferred = matches.FirstOrDefault(
            a => a.Name.EndsWith(preferredExt, StringComparison.OrdinalIgnoreCase));

        return preferred ?? matches[0];
    }

    /// <summary>
    /// Pure mapping from OS + architecture to a RID. Exposed internally so
    /// tests can exercise every supported pair (and the unsupported throw
    /// paths) without spoofing <see cref="RuntimeInformation"/>.
    /// </summary>
    internal static string Resolve(OSPlatform os, Architecture architecture)
    {
        string osSegment;
        if (os == OSPlatform.Windows)
        {
            osSegment = "win";
        }
        else if (os == OSPlatform.OSX)
        {
            osSegment = "osx";
        }
        else if (os == OSPlatform.Linux)
        {
            osSegment = "linux";
        }
        else
        {
            throw new GracefulException(
                $"Unsupported operating system '{os}'. The func CLI ships binaries for Windows, macOS, and Linux only.",
                isUserError: true);
        }

        string archSegment = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new GracefulException(
                $"Unsupported process architecture '{architecture}'. The func CLI ships x64 and arm64 binaries only.",
                isUserError: true),
        };

        string rid = $"{osSegment}-{archSegment}";

        // Defensive: SupportedRids is the single source of truth shared with
        // the install scripts; the switches above should never produce a value
        // outside it, but assert that invariant rather than trust it silently.
        if (Array.IndexOf(_supportedRids, rid) < 0)
        {
            throw new GracefulException(
                $"Resolved RID '{rid}' is not in the supported set.",
                isUserError: false);
        }

        return rid;
    }

    private static OSPlatform GetCurrentOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OSPlatform.OSX;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OSPlatform.Linux;
        }

        throw new GracefulException(
            "Unsupported operating system. The func CLI runs on Windows, macOS, and Linux only.",
            isUserError: true);
    }
}
