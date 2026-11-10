// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplaterFactory(
    CliConfigurationPathsOptions configurationPaths,
    Lazy<FuncTemplateConstraintEvaluator> funcConstraintEvaluator,
    IFileSystem fileSystem) : ITemplaterFactory
{
    /// <summary>
    /// Sub-directory (under the func home) that holds the engine's settings,
    /// installed template packages, and caches.
    /// </summary>
    private const string SettingsDirectoryName = "templates";
    internal const int MaximumPackageCount = 4;
    internal const int MaximumArchiveBytes = 4 * 1024 * 1024;
    internal const int MaximumEntryCount = 512;
    internal const int MaximumEntryBytes = 1024 * 1024;
    internal const int MaximumConfigurationBytes = 64 * 1024;
    internal const int MaximumContentBytes = 32 * 1024 * 1024;

    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));
    private readonly Lazy<FuncTemplateConstraintEvaluator> _funcConstraintEvaluator =
        funcConstraintEvaluator ?? throw new ArgumentNullException(nameof(funcConstraintEvaluator));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public Templater Create(TemplateEngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string settingsLocation = Path.Combine(_configurationPaths.Home, SettingsDirectoryName);
        return CreateTemplater(new TemplateEngineSession(context, settingsLocation));
    }

    public async Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ITemplatePackage> registered;
        // Package registration is inspected without asking the engine to discover or mount installed content.
        using (TemplateEngineSession registration = new(context, Path.Combine(_configurationPaths.Home, SettingsDirectoryName)))
        {
            registered = await registration.PackageManager.GetTemplatePackagesAsync(false, cancellationToken);
        }

        ITemplatePackage[] packages = [.. registered.DistinctBy(package => package.MountPointUri, StringComparer.Ordinal)];
        if (packages.Length is 0 or > MaximumPackageCount)
        {
            throw new InvalidDataException($"Generation requires between 1 and {MaximumPackageCount} registered local template packages.");
        }

        if (packages.Any(package => package is not IManagedTemplatePackage
            || !Path.IsPathFullyQualified(package.MountPointUri)
            || !Path.GetExtension(package.MountPointUri).Equals(".nupkg", StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException("Private generation currently supports only registered local NuGet template archives. Folder sources remain disabled.");
        }

        IManagedTemplatePackage[] managedPackages = [.. packages.Cast<IManagedTemplatePackage>()];
        if (managedPackages.Select(package => package.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Length)
        {
            throw new InvalidDataException("Registered template packages contain conflicting package identities.");
        }

        string root = Path.Combine(_configurationPaths.Home, "template-operations", Guid.NewGuid().ToString("N"));
        PrivateTemplateFileSystem memory = new(root, Path.Combine(root, "output"));
        TemplateEngineSession? operation = null;
        bool transferred = false;
        try
        {
            List<string> capturedPaths = [];
            long contentBytes = 0;
            foreach (ITemplatePackage package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using Stream original = _fileSystem.OpenReadNoFollow(package.MountPointUri, cancellationToken);
                byte[] archive = await ReadBoundedAsync(original, MaximumArchiveBytes, cancellationToken);
                contentBytes += await ValidateArchiveAsync(archive, memory, root, cancellationToken);
                if (contentBytes > MaximumContentBytes)
                {
                    throw new InvalidDataException("Registered template content exceeds the operation byte limit.");
                }

                string capturedPath = Path.Combine(root, "content", $"{capturedPaths.Count}.nupkg");
                memory.CreateDirectory(Path.GetDirectoryName(capturedPath)!);
                using (Stream output = memory.CreateFile(capturedPath))
                {
                    await output.WriteAsync(archive, cancellationToken);
                }

                capturedPaths.Add(capturedPath);
            }

            memory.SealSource(Path.Combine(root, "content"));
            operation = new(context, Path.Combine(root, "settings"), memory);
            IManagedTemplatePackageProvider provider = operation.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            IReadOnlyList<InstallResult> results = await provider.InstallAsync(
                [.. capturedPaths.Select(path => new InstallRequest(path))], cancellationToken);
            if (results.Count != capturedPaths.Count || results.Any(result => !result.Success))
            {
                throw new InvalidDataException($"Captured template registration failed: {string.Join("; ", results.Where(result => !result.Success).Select(result => result.ErrorMessage))}");
            }

            IReadOnlyList<ITemplatePackage> installed = await operation.PackageManager.GetTemplatePackagesAsync(false, cancellationToken);
            if (installed.Count != managedPackages.Length
                || installed.Any(package => package is not IManagedTemplatePackage staged
                    || !managedPackages.Any(original =>
                        string.Equals(original.Identifier, staged.Identifier, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(original.Version, staged.Version, StringComparison.Ordinal))))
            {
                throw new InvalidDataException("Captured template registration does not match the installed package identities and versions.");
            }

            foreach (ITemplatePackage package in installed)
            {
                memory.SealSource(package.MountPointUri);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Templater templater = CreateTemplater(operation);
            transferred = true;
            return templater;
        }
        finally
        {
            if (!transferred)
            {
                if (operation is not null)
                {
                    operation.Dispose();
                }
                else
                {
                    memory.Dispose();
                }
            }
        }
    }

    private Templater CreateTemplater(TemplateEngineSession session)
    {
        TemplateEligibilityEvaluator eligibilityEvaluator = new(session, _funcConstraintEvaluator);
        return new(session, new TemplateSelection(session, eligibilityEvaluator));
    }

    private static async Task<long> ValidateArchiveAsync(
        byte[] bytes, PrivateTemplateFileSystem memory, string root, CancellationToken cancellationToken)
    {
        using MemoryStream content = new(bytes, writable: false);
        using ZipArchive archive = new(content, ZipArchiveMode.Read);
        if (archive.Entries.Count is 0 or > MaximumEntryCount)
        {
            throw new InvalidDataException("Template archive exceeds its entry limit or is empty.");
        }

        Dictionary<string, (string ExactPath, bool Directory)> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> explicitPaths = new(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName;
            bool directory = name.EndsWith('/');
            string[] segments = (directory ? name[..^1] : name).Split('/');
            int unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if (name.Length > 1024 || name.Contains('\\') || segments.Length > 16
                || segments.Any(segment => segment.Length is 0 or > 255 || segment is "." or "..")
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || unixType is not (0 or 0x8000 or 0x4000)
                || (unixType == 0x4000 && !directory)
                || !explicitPaths.Add(string.Join('/', segments)))
            {
                throw new InvalidDataException($"Template archive has an unsafe or duplicate entry '{name}'.");
            }

            for (int index = 0; index < segments.Length; index++)
            {
                string path = string.Join('/', segments.Take(index + 1));
                bool isDirectory = directory || index < segments.Length - 1;
                if (paths.TryGetValue(path, out (string ExactPath, bool Directory) existing)
                    && (!string.Equals(path, existing.ExactPath, StringComparison.Ordinal) || existing.Directory != isDirectory))
                {
                    throw new InvalidDataException($"Template archive has conflicting paths at '{name}'.");
                }

                paths.TryAdd(path, (path, isDirectory));
            }

            // Use the same path admission as the private filesystem, without extracting archive content.
            _ = memory.FileExists(Path.Combine(root, "content", Path.Combine(segments)));

            int limit = segments.Contains(".template.config", StringComparer.OrdinalIgnoreCase)
                ? MaximumConfigurationBytes : MaximumEntryBytes;
            if (entry.Length > limit || (directory && entry.Length != 0))
            {
                throw new InvalidDataException($"Template archive entry '{name}' exceeds its byte limit.");
            }

            using Stream input = entry.Open();
            byte[] validated = await ReadBoundedAsync(input, limit, cancellationToken);
            if (validated.LongLength != entry.Length)
            {
                throw new InvalidDataException($"Template archive entry '{name}' has inconsistent content length.");
            }

            totalBytes += validated.LongLength;
            if (totalBytes > MaximumContentBytes)
            {
                throw new InvalidDataException("Template archive exceeds its expanded byte limit.");
            }
        }

        return totalBytes;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using MemoryStream result = new();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int remaining = limit - checked((int)result.Length);
            int count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), cancellationToken);
            if (count == 0)
            {
                return result.ToArray();
            }

            if (count > remaining)
            {
                throw new InvalidDataException($"Template content exceeds its {limit}-byte limit.");
            }

            await result.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }
}
