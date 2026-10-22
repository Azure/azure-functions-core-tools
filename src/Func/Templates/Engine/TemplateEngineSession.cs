// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Abstractions.Parameters;
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
    private readonly NativeTemplatePackageScope? _packageScope;
    private readonly Dictionary<string, string> _packagePins = new(StringComparer.Ordinal);
    private readonly IDisposable? _ownership;
    private readonly TemplateStoreEngineDiagnostics? _diagnostics;
    private NativeMsBuildBindingSnapshot? _msBuildBindings;
    private bool _disposed;

    public TemplateEngineSession(TemplateEngineContext context, string settingsLocation, TemplateConstraintEvaluationSnapshot? snapshot = null,
        IDisposable? ownership = null, TemplateStoreEngineDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);

        Context = context;
        _packageScope = snapshot?.PackageScope;
        _ownership = ownership;
        _diagnostics = diagnostics;
        _host = new(context, snapshot, diagnostics);
        _settings = new(_host, settingsLocation: settingsLocation);
        _packageManager = new(_settings);
        _constraintManager = new(_settings);
        _creator = new(_settings);
    }

    public TemplateEngineContext Context { get; }

    internal async Task<NativeTemplatePackageDisposition> ClassifyPackageScopeAsync(ITemplatePackage? package, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        NativeTemplatePackageDisposition result = _packageScope is null ? new(true)
            : await _packageScope.ClassifyAsync(PackageInfo(package), cancellationToken);
        if (!result.Include && result.OwnedSha256 is { } hash && package is not null)
        {
            try
            {
                if (await ValidatePackageHashAsync(package.MountPointUri, hash, cancellationToken) is { } diagnostic)
                {
                    return new(true, "The claimed inactive native registration is not proven owned. " + diagnostic);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(true, $"The claimed inactive native registration cannot be validated: {exception.Message}");
            }
        }
        return result;
    }

    internal async Task<string?> ValidatePackageScopeAsync(ITemplatePackage? package, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_packageScope is null || package is null || PackageInfo(package) is not { } info)
        {
            return null;
        }
        (string? hash, string? diagnostic) = await _packageScope.VerifyAsync(info, cancellationToken);
        if (diagnostic is not null || hash is null)
        {
            return diagnostic;
        }
        string? failure = await ValidatePackageHashAsync(package.MountPointUri, hash, cancellationToken);
        if (failure is null)
        {
            _packagePins[package.MountPointUri] = hash;
        }
        return failure;
    }

    private Task<string?> ValidatePackageHashAsync(string archive, string expected, CancellationToken cancellationToken)
    {
        NativeTemplatePackageScope scope = _packageScope
            ?? throw new InvalidOperationException("Native archive verification requires a captured package scope.");
        return scope.ValidateArchiveAsync(_settings.Host.FileSystem, archive, expected, cancellationToken);
    }

    private static TemplatePackageInfo? PackageInfo(ITemplatePackage? package) =>
        package is IManagedTemplatePackage managed ? new(managed.Identifier, managed.Version) : null;

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

    internal void CheckStoreDiagnostics()
    {
        _host.ThrowIfCohortFailed();
        _diagnostics?.ThrowIfCacheFailed();
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
            DefaultName = string.IsNullOrEmpty(loaded.DefaultName) ? null : loaded.DefaultName,
            HostConfigPlace = hostFile?.FullPath,
            HostData = hostData,
        };
    }

    internal async Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(ITemplateInfo template, string relativePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Replace('\\', '/').Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Supply an exact package-relative source path without traversal.", nameof(relativePath));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_settings.Components.TryGetComponent(template.GeneratorId, out IGenerator? generator) || generator is null)
        {
            throw new InvalidTemplateMetadataException($"Selected template '{template.Identity}' has no registered generator.");
        }

        ITemplate? loaded = await generator.LoadTemplateAsync(_settings, template, null, cancellationToken);
        using var lifetime = loaded as IDisposable;
        if (loaded?.Configuration is not IFile configuration)
        {
            throw new InvalidTemplateMetadataException($"Selected template '{template.Identity}' has no mounted package source.");
        }

        string mountedPath = configuration.MountPoint.Root.FullPath.TrimEnd('/', '\\') + "/" + relativePath.Replace('\\', '/');
        IFile? source = configuration.MountPoint.FileInfo(mountedPath);
        if (source is null || !source.Exists)
        {
            throw new FileNotFoundException("The source-qualified template package file does not exist.", relativePath);
        }

        using Stream stream = source.OpenRead();
        using MemoryStream content = new();
        await stream.CopyToAsync(content, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return content.ToArray();
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

    Task<ReadOnlyMemory<byte>> ITemplateInvocationEngine.ReadPackageFileAsync(ITemplateInfo template, string relativePath, CancellationToken cancellationToken)
        => ReadPackageFileAsync(template, relativePath, cancellationToken);

    Task<string?> ITemplateInvocationEngine.ValidatePinnedPackageAsync(ITemplateInfo template, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return _packagePins.TryGetValue(template.MountPointUri, out string? expected)
            ? ValidatePackageHashAsync(template.MountPointUri, expected, cancellationToken)
            : Task.FromResult<string?>(null);
    }

    string? ITemplateInvocationEngine.QualifyMsBuildBindings(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request)
    {
        ThrowIfDisposed();
        if (request.MsBuildBindings is not { } snapshot)
        {
            if (_msBuildBindings is not null)
            {
                return "This controlled session requires its exact explicit MSBuild snapshot on every invocation.";
            }

            if (configuration.Type == TemplateType.Item && request.EffectSupport is not null
                && configuration.Root.TryGetProperty("symbols", out JsonElement symbols) && symbols.ValueKind == JsonValueKind.Object
                && symbols.EnumerateObject().Any(symbol => symbol.Value.ValueKind == JsonValueKind.Object
                    && symbol.Value.TryGetProperty("binding", out JsonElement binding) && binding.ValueKind == JsonValueKind.String
                    && binding.GetString()!.StartsWith("msbuild:", StringComparison.OrdinalIgnoreCase)))
            {
                return "Controlled item effects require an explicit evaluated MSBuild snapshot; template framework defaults are not a project binding.";
            }

            return null;
        }

        NativeTemplateInvocationEffects? effects = request.EffectSupport;
        if (configuration.Type != TemplateType.Item || !string.Equals(Context.Project?.Stack, "dotnet", StringComparison.OrdinalIgnoreCase)
            || effects is null || effects.ProjectDirectory is null
            || snapshot.TemplateIdentity != template.Identity
            || snapshot.SourceIdentity != $"{template.MountPointUri}::{template.ConfigPlace}"
            || !string.Equals(snapshot.SourceFingerprint, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(effects.ExactProjectPath)
            || !SamePath(snapshot.ProjectFilePath, effects.ExactProjectPath)
            || Context.Project is null || !SamePath(Context.Project.RootDirectory.Info.FullName, effects.ProjectDirectory.Info.FullName))
        {
            return "The MSBuild snapshot does not match the exact selected item, mounted configuration, project or controlled effects boundary.";
        }

        string relative = Path.GetRelativePath(Context.Project.RootDirectory.Info.FullName, snapshot.ProjectFilePath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return "The evaluated MSBuild project is outside the selected Functions project.";
        }

        if (_msBuildBindings is not null)
        {
            return _msBuildBindings == snapshot ? null : "A controlled native session cannot replace its immutable MSBuild snapshot.";
        }

        _settings.Components.AddComponent(typeof(IBindSymbolSource), new NativeMsBuildBindingSnapshotSource(snapshot));
        _msBuildBindings = snapshot;
        return null;
    }

    IReadOnlyDictionary<string, object> ITemplateInvocationEngine.GetPopulatedParameters(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request)
    {
        ThrowIfDisposed();
        if (!_settings.Components.TryGetComponent(template.GeneratorId, out IGenerator? generator) || generator is null)
        {
            throw new InvalidOperationException($"Selected template '{template.Identity}' has no registered generator.");
        }

        string? name = !string.IsNullOrEmpty(request.Name) ? request.Name
            : template.PreferDefaultName || string.IsNullOrEmpty(request.OutputDirectory.Info.Name) ? template.DefaultName
            : request.OutputDirectory.Info.Name;
        Dictionary<string, object> values = new(StringComparer.Ordinal);
        foreach (ITemplateParameter parameter in configuration.Parameters)
        {
            if (parameter.Precedence.PrecedenceDefinition is PrecedenceDefinition.Disabled or PrecedenceDefinition.ConditionalyDisabled)
            {
                continue;
            }

            string? value = null;
            if (!parameter.IsName)
            {
                _settings.Host.TryGetHostParamDefault(parameter.Name, out value);
                if (value is null && !parameter.Precedence.CanBeRequired)
                {
                    value = parameter.DefaultValue;
                }
            }

            object? converted = parameter.IsName ? name : null;
            if (request.Parameters.TryGetValue(parameter.Name, out string? supplied))
            {
                value = supplied ?? parameter.DefaultIfOptionWithoutValue;
            }

            if (value is not null)
            {
                converted = generator.ConvertParameterValueToType(_settings, parameter, value, out bool error);
                if (error)
                {
                    continue;
                }
            }

            if (converted is not null)
            {
                values[parameter.Name] = converted;
            }
        }

        return values;
    }

    Task<ITemplateCreationResult> ITemplateInvocationEngine.InstantiateAsync(
        ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
    }

    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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

    protected virtual async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
        InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
    {
        IPhysicalFileSystem? previous = _host.PreflightFileSystem;
        if (dryRun && request.PreviewAfterDirectoryCleanup && request.DryRun && request.Force
            && TemplateCatalogProjector.GetTemplateType(template) == TemplateType.Project)
        {
            _host.PreflightFileSystem = new PlannedCleanupTemplateFileSystem(_host.FileSystem, request.OutputDirectory.Info.FullName);
        }
        try
        {
            return await _creator.InstantiateAsync(template, request.Name, request.OutputDirectory.Info.Name, request.OutputDirectory.Info.FullName,
                parameters, forceCreation: dryRun || request.Force, dryRun: dryRun, cancellationToken: cancellationToken);
        }
        finally
        {
            _host.PreflightFileSystem = previous;
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
