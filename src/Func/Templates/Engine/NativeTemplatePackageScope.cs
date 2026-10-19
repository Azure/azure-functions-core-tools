// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;
using Azure.Functions.Cli.Workloads.Install;
using System.Security.Cryptography;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed record NativeTemplatePackagePin(string Identifier, string Version, string Sha256)
{
    public BundledTemplateOrigin? Origin { get; init; }
}

internal sealed record NativeTemplateOwnership(bool Proven, string? Diagnostic = null, string? Sha256 = null);

internal sealed record NativeTemplatePackageDisposition(bool Include, string? Diagnostic = null, string? OwnedSha256 = null);

internal delegate Task<NativeTemplateOwnership> NativeTemplateOwnershipReader(
    NativeTemplatePackagePin? expected, TemplatePackageInfo package, CancellationToken cancellationToken);

/// <summary>
/// Restricts bundled packages to exact assets of the successfully configured boot cohort, not the newest native version.
/// </summary>
internal sealed class NativeTemplatePackageScope(IReadOnlyList<NativeTemplatePackagePin> owned, IReadOnlyList<NativeTemplatePackagePin> active,
    NativeTemplateOwnershipReader? ownership = null)
{
    private readonly IReadOnlyList<NativeTemplatePackagePin> _owned = Freeze(owned, nameof(owned));
    private readonly IReadOnlyList<NativeTemplatePackagePin> _active = Freeze(active, nameof(active));

    public NativeTemplatePackageScope ForPhysicalOwner(string packageId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return new(_owned, Array.AsReadOnly<NativeTemplatePackagePin>([.. _active.Where(pin =>
            pin.Origin is { } origin && string.Equals(origin.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                && origin.Version == version)]), ownership);
    }

    public bool Includes(TemplatePackageInfo? package) => package is null
        || !_owned.Any(pin => Matches(pin, package))
        || _active.Any(pin => Matches(pin, package));

    public bool IsBundledFamily(TemplatePackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _owned.Any(pin => SameId(pin.Identifier, package.Identifier));
    }

    public async Task<NativeTemplatePackageDisposition> ClassifyAsync(TemplatePackageInfo? package, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (package is null || !_owned.Any(pin => SameId(pin.Identifier, package.Identifier)) || _active.Any(pin => Matches(pin, package)))
        {
            return new(true);
        }
        if (ownership is null)
        {
            return new(Includes(package));
        }
        NativeTemplateOwnership result = await ownership(null, package, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Proven && result.Sha256 is { Length: 64 } hash && hash.All(Uri.IsHexDigit)
            ? new(false, OwnedSha256: hash) : new(true, result.Diagnostic
            ?? $"Native package '{package.Identifier}@{package.Version}' has unknown ownership outside the captured cohort. User-installed content was not hidden or replaced.");
    }

    public async Task<(string? Sha256, string? Diagnostic)> VerifyAsync(TemplatePackageInfo package, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (string? hash, string? diagnostic) = ExpectedHash(package);
        if (diagnostic is not null || hash is null || ownership is null)
        {
            return (hash, diagnostic);
        }
        NativeTemplatePackagePin expected = _active.First(pin => Matches(pin, package));
        NativeTemplateOwnership result = await ownership(expected, package, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Proven ? (hash, null) : (null, result.Diagnostic
            ?? $"Native package '{package.Identifier}@{package.Version}' does not have the captured workload's exact ownership receipt. Explicitly repair its registration before retrying.");
    }

    public (string? Sha256, string? Diagnostic) ExpectedHash(TemplatePackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string[] hashes = [.. _active.Where(pin => Matches(pin, package)).Select(pin => pin.Sha256).Distinct(StringComparer.OrdinalIgnoreCase)];
        return hashes.Length switch
        {
            0 when _owned.Any(pin => SameId(pin.Identifier, package.Identifier)) =>
                (null, $"Native package '{package.Identifier}@{package.Version}' is not an active owned asset of the captured workload cohort."),
            0 => (null, null),
            1 => (hashes[0], null),
            _ => (null, $"The captured workload cohort declares conflicting hashes for native package '{package.Identifier}@{package.Version}'. Repair the workload installation before retrying."),
        };
    }

    internal NativeTemplatePackagePin? ReceiptBoundPin(TemplatePackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (ownership is null)
        {
            return null;
        }

        NativeTemplatePackagePin[] matches = [.. _active.Where(pin => Matches(pin, package)).Distinct()];
        return matches is [{ Origin: not null } pin] ? pin : null;
    }

    public async Task<string?> ValidateArchiveAsync(IPhysicalFileSystem files, string archive, string expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);
        cancellationToken.ThrowIfCancellationRequested();
        if (!files.FileExists(archive))
        {
            return "The captured native package archive is missing. Explicitly repair the retained workload installation before retrying.";
        }
        using Stream stream = files.OpenRead(archive);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ? null
            : "The native package differs from the captured workload asset hash. Explicitly repair the workload installation before retrying; no fallback version was selected.";
    }

    private static bool Matches(NativeTemplatePackagePin pin, TemplatePackageInfo package) => SameId(pin.Identifier, package.Identifier)
        && NuGetVersion.TryParse(pin.Version, out NuGetVersion? expected)
        && NuGetVersion.TryParse(package.Version, out NuGetVersion? actual)
        && expected == actual;

    private static bool SameId(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<NativeTemplatePackagePin> Freeze(IReadOnlyList<NativeTemplatePackagePin> pins, string parameter)
    {
        ArgumentNullException.ThrowIfNull(pins, parameter);
        if (pins.Any(pin => pin is null || string.IsNullOrWhiteSpace(pin.Identifier)
            || !NuGetVersion.TryParse(pin.Version, out _) || string.IsNullOrWhiteSpace(pin.Sha256)
            || pin.Sha256.Length != 64 || pin.Sha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException("Native package pins require an identity, semantic version and SHA-256 digest.", parameter);
        }
        return Array.AsReadOnly<NativeTemplatePackagePin>([.. pins]);
    }
}
