// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Azure.Functions.Cli.Templates.Acquisition;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Owns the template engine host, environment and package manager for one command.
/// </summary>
/// <remarks>
/// Engine components capture the environment when they are created, so every phase of a command shares this
/// session instead of building a new environment. Once disposed, the session refuses further use rather than
/// letting callers create a replacement environment without the command context.
/// </remarks>
internal class TemplateEngineSession : ITemplateInvocationEngine, IDisposable
{
    private readonly EngineEnvironmentSettings _settings;
    private readonly FuncTemplateEngineHost _host;
    private readonly TemplatePackageManager _packageManager;
    private readonly TemplateConstraintManager _constraintManager;
    private readonly TemplateCreator _creator;
    private readonly IDisposable? _ownership;
    private readonly TemplateStoreEngineDiagnostics? _diagnostics;
    private bool _disposed;
    private int _boundOperation;

    public TemplateEngineSession(TemplateEngineContext context, string settingsLocation, TemplateConstraintEvaluationSnapshot? snapshot = null,
        IDisposable? ownership = null, TemplateStoreEngineDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);

        Context = context;
        _ownership = ownership;
        _diagnostics = diagnostics;
        _host = new(context, snapshot, diagnostics);
        _settings = new(_host, settingsLocation: settingsLocation);
        _packageManager = new(_settings);
        _constraintManager = new(_settings);
        _creator = new(_settings);
    }

    public TemplateEngineContext Context { get; }

    public IEngineEnvironmentSettings Settings
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _settings;
        }
    }

    public TemplatePackageManager PackageManager
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _packageManager;
        }
    }

    internal async Task RebuildCacheAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await _packageManager.RebuildTemplateCacheAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _diagnostics?.ThrowIfCacheFailed();
        string cache = Path.Combine(_settings.Paths.HostVersionSettingsDir, "templatecache.json");
        if (!_settings.Host.FileSystem.FileExists(cache))
        {
            throw new IOException("The template engine did not persist its rebuilt cache.");
        }
    }

    internal void CheckStoreDiagnostics() => _diagnostics?.ThrowIfCacheFailed();

    internal IDisposable EnterBoundOperation()
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _boundOperation, 1, 0) != 0)
        {
            throw new InvalidOperationException("An artifact-bound template flow cannot be used concurrently.");
        }
        return new BoundOperation(this);
    }

    internal async Task<TemplateArtifactIdentity?> ReadCurrentArtifactAsync(ITemplateInfo template, CancellationToken cancellationToken)
    {
        IManagedTemplatePackage? package = await FindCurrentPackageAsync(template, cancellationToken);
        if (package is null)
        {
            return null;
        }
        using Stream stream = _settings.Host.FileSystem.OpenRead(package.MountPointUri);
        string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return new(package.Identifier, package.Version!, digest);
    }

    internal async Task ValidateCurrentArtifactAsync(ITemplateInfo template, BasicTemplateArtifact artifact,
        IBasicArchiveInspector inspector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(inspector);
        IManagedTemplatePackage? package = await FindCurrentPackageAsync(template, cancellationToken);
        if (package is null || !string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase)
            || package.Version != artifact.Version.ToNormalizedString())
        {
            throw new InvalidTemplateMetadataException("The selected template is not mounted from the authorized exact package.");
        }
        using Stream stream = _settings.Host.FileSystem.OpenRead(package.MountPointUri);
        await inspector.ValidateArchiveAsync(stream, artifact, cancellationToken);
        IReadOnlyList<ITemplateInfo> templates = await _packageManager.GetTemplatesAsync(cancellationToken);
        string[] identities = [.. templates.Where(item => item.MountPointUri == package.MountPointUri).Select(item => item.Identity)];
        CheckStoreDiagnostics();
        if (identities.Length != artifact.Templates.Count
            || identities.Distinct(StringComparer.Ordinal).Count() != identities.Length
            || artifact.Templates.Any(item => !identities.Contains(item.Identity, StringComparer.Ordinal)))
        {
            throw new InvalidTemplateMetadataException("The current engine inventory differs from the qualified archive inventory.");
        }
        if (!_settings.Components.TryGetComponent(template.GeneratorId, out IGenerator? generator) || generator is null)
        {
            throw new InvalidTemplateMetadataException("The bound template has no registered generator.");
        }
        ITemplate? loaded = await generator.LoadTemplateAsync(_settings, template, null, cancellationToken);
        using var lifetime = loaded as IDisposable;
        if (loaded?.Configuration is not IFile configuration)
        {
            throw new InvalidTemplateMetadataException("The bound template has no mounted archive configuration.");
        }
        stream.Position = 0;
        using PackageArchiveReader reader = new(stream, leaveStreamOpen: true);
        var mountedFiles = configuration.MountPoint.Root.EnumerateFiles("*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(configuration.MountPoint.Root.FullPath, file.FullPath).Replace('\\', '/'), StringComparer.Ordinal);
        string[] paths = [.. await reader.GetFilesAsync(cancellationToken)];
        if (paths.Length != mountedFiles.Count)
        {
            throw new InvalidTemplateMetadataException("The current mount has a different file inventory from the qualified archive.");
        }
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mountedFiles.TryGetValue(path, out IFile? file) || !file.Exists)
            {
                throw new InvalidTemplateMetadataException("The current mount is missing qualified archive content.");
            }
            using Stream expected = await reader.GetStreamAsync(path, cancellationToken);
            using Stream mounted = file.OpenRead();
            byte[] expectedHash = await SHA256.HashDataAsync(expected, cancellationToken);
            byte[] mountedHash = await SHA256.HashDataAsync(mounted, cancellationToken);
            if (!expectedHash.AsSpan().SequenceEqual(mountedHash))
            {
                throw new InvalidTemplateMetadataException("The current mounted content differs from the qualified archive bytes. Open a fresh template session.");
            }
        }
    }

    private async Task<IManagedTemplatePackage?> FindCurrentPackageAsync(ITemplateInfo template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IManagedTemplatePackage> packages = await _packageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package => package.MountPointUri == template.MountPointUri)];
        CheckStoreDiagnostics();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Version)
            || _settings.Host.FileSystem.DirectoryExists(matches[0].MountPointUri))
        {
            return null;
        }
        return matches[0];
    }

    public virtual async Task<TemplateConfigurationSnapshot> ReadConfigurationAsync(ITemplateInfo template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_settings.Components.TryGetComponent(template.GeneratorId, out IGenerator? generator) || generator is null)
        {
            throw new InvalidTemplateMetadataException($"Template '{template.Identity}' has no registered generator.");
        }

        ITemplate? loaded = await generator.LoadTemplateAsync(_settings, template, null, cancellationToken);
        if (loaded is null)
        {
            throw new InvalidTemplateMetadataException($"Template '{template.Identity}' could not be loaded from its mounted content.");
        }

        using var lifetime = loaded as IDisposable;
        if (loaded.Configuration is not IFile configurationFile)
        {
            throw new InvalidTemplateMetadataException($"Template '{template.Identity}' configuration is not a mounted file.");
        }

        using Stream stream = configurationFile.OpenRead();
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, cancellationToken);
        string fingerprint = Convert.ToHexString(SHA256.HashData(content.GetBuffer().AsSpan(0, checked((int)content.Length))));
        content.Position = 0;
        using var reader = new StreamReader(content);
        string configuration = await reader.ReadToEndAsync(cancellationToken);
        IFile? hostFile = loaded.HostSpecificConfiguration is IFile { Exists: true } selectedHostFile
            ? selectedHostFile
            : configurationFile.Parent?.EnumerateFiles(FuncHostTemplateMetadataReader.FileName, SearchOption.TopDirectoryOnly).SingleOrDefault();
        string? hostData = null;
        if (hostFile is not null)
        {
            using Stream hostStream = hostFile.OpenRead();
            using var hostReader = new StreamReader(hostStream);
            hostData = await hostReader.ReadToEndAsync(cancellationToken);
        }

        return new(configuration, fingerprint, TemplateCatalogProjector.GetTemplateType(loaded), Array.AsReadOnly<ITemplateParameter>([.. loaded.ParameterDefinitions]))
        {
            HostConfigPlace = hostFile?.FullPath,
            HostData = hostData,
        };
    }

    public virtual async Task<IReadOnlyCollection<string>> ReadPackageTypesAsync(ITemplatePackage? package, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (package is not IManagedTemplatePackage || _settings.Host.FileSystem.DirectoryExists(package.MountPointUri))
        {
            return [];
        }

        try
        {
            using Stream stream = _settings.Host.FileSystem.OpenRead(package.MountPointUri);
            using var reader = new PackageArchiveReader(stream);
            IEnumerable<NuGet.Packaging.Core.PackageType> types = await reader.GetPackageTypesAsync(cancellationToken);
            return Array.AsReadOnly<string>([.. types.Select(type => type.Name)]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidTemplateMetadataException("Installed package host metadata could not be read.", exception);
        }
        catch (Exception exception) when (exception is InvalidDataException or PackagingException or XmlException)
        {
            throw new InvalidTemplateMetadataException("Installed package host metadata is invalid.", exception);
        }
    }

    public Task<TemplateConstraintEvaluation> EvaluateConstraintsAsync(string configuration, CancellationToken cancellationToken, IReadOnlyCollection<string>? packageTypes = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return EvaluateConstraintsAsync(TemplateConfigurationSnapshot.ParseRoot(configuration), cancellationToken, packageTypes);
    }

    public async Task<TemplateConstraintEvaluation> EvaluateConstraintsAsync(
        JsonElement configuration, CancellationToken cancellationToken, IReadOnlyCollection<string>? packageTypes = null)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TemplateConstraintDeclaration> declarations = TemplateConstraintDeclarationReader.Read(configuration, packageTypes);
        if (configuration.ValueKind == JsonValueKind.Object && configuration.EnumerateObject().Any(property =>
            string.Equals(property.Name, "additionalConfigFiles", StringComparison.OrdinalIgnoreCase)
            && (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 0)))
        {
            throw new InvalidTemplateMetadataException("Supplemental template configuration requires a qualified complete configuration inventory before this flow can use it.");
        }

        List<TemplateConstraintDiagnostic> diagnostics = [];
        TemplateConstraintEvaluationStatus status = TemplateConstraintEvaluationStatus.Eligible;
        foreach (TemplateConstraintDeclaration declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateConstraintResult result = await _constraintManager.EvaluateConstraintAsync(declaration.Type, declaration.Arguments?.GetRawText(), cancellationToken);
            if (result.EvaluationStatus == TemplateConstraintResult.Status.Allowed)
            {
                continue;
            }

            bool recognized = _settings.Components.OfType<ITemplateConstraintFactory>().Any(factory => factory.Type == declaration.Type);
            TemplateConstraintEvaluationStatus current = result.EvaluationStatus == TemplateConstraintResult.Status.Restricted
                ? TemplateConstraintEvaluationStatus.Restricted
                : recognized ? TemplateConstraintEvaluationStatus.Failed : TemplateConstraintEvaluationStatus.NotEvaluated;
            if (current > status)
            {
                status = current;
            }

            diagnostics.Add(new(declaration.Label, declaration.Type, current, result.LocalizedErrorMessage ?? "Constraint could not be evaluated.", result.CallToAction));
        }

        return new(status, diagnostics.AsReadOnly());
    }

    IPhysicalFileSystem ITemplateInvocationEngine.FileSystem
    {
        get
        {
            ThrowIfDisposed();
            return _host.FileSystem;
        }
    }

    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class BoundOperation(TemplateEngineSession session) : IDisposable
    {
        private TemplateEngineSession? _session = session;

        public void Dispose()
        {
            TemplateEngineSession? owner = Interlocked.Exchange(ref _session, null);
            if (owner is not null)
            {
                Interlocked.Exchange(ref owner._boundOperation, 0);
            }
        }
    }

    async Task<ITemplateCreationResult> ITemplateInvocationEngine.RenderImageAsync(
        ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, TemplateOutputImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OutputDirectory);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!image.IsBoundTo(_host.FileSystem, request.OutputDirectory.Info.FullName))
        {
            throw new ArgumentException("The output image must use this session's filesystem and requested output directory.", nameof(image));
        }
        _host.InvocationFileSystem.BeginCapture(image);
        try
        {
            ITemplateCreationResult result = await InstantiateAsync(template, request, parameters, false, cancellationToken);
            if (result.Status == CreationResultStatus.Success)
            {
                image.Seal();
            }
            return result;
        }
        finally
        {
            _host.InvocationFileSystem.EndCapture(image);
        }
    }

    protected virtual Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        => _creator.InstantiateAsync(template, request.Name, request.OutputDirectory.Info.Name, request.OutputDirectory.Info.FullName, parameters,
            forceCreation: dryRun || request.Force, dryRun: dryRun, cancellationToken: cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _constraintManager.Dispose();
        }
        finally
        {
            try
            {
                _packageManager.Dispose();
            }
            finally
            {
                try
                {
                    _settings.Dispose();
                }
                finally
                {
                    _ownership?.Dispose();
                }
            }
        }
    }
}
