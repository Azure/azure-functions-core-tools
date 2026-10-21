// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.ConfigModel;
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
internal class TemplateEngineSession : IDisposable
{
    private readonly EngineEnvironmentSettings _settings;
    private readonly TemplatePackageManager _packageManager;
    private readonly TemplateConstraintManager _constraintManager;
    private readonly TemplateCreator _creator;
    private readonly FuncTemplateEngineHost _host;
    private readonly NativeTemplatePackageScope? _packageScope;
    private readonly Dictionary<string, string> _packagePins = new(StringComparer.Ordinal);
    private readonly TemplateProjectSupport? _projectSupport;
    private readonly IDisposable? _ownership;
    private readonly TemplateStoreEngineDiagnostics? _diagnostics;
    private NativeMsBuildBindingSnapshot? _msBuildBindings;
    private readonly Dictionary<string, DotNetProjectLayoutQualification> _projectLayouts = new(StringComparer.Ordinal);
    private bool _disposed;

    public TemplateEngineSession(TemplateEngineContext context, string settingsLocation, TemplateConstraintEvaluationSnapshot? snapshot = null,
        TemplateProjectSupport? projectSupport = null,
        IDisposable? ownership = null, TemplateStoreEngineDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);
        Context = context;
        _packageScope = snapshot?.PackageScope;
        _projectSupport = projectSupport;
        _ownership = ownership;
        _diagnostics = diagnostics;
        _host = new(context, snapshot, diagnostics);
        _settings = new(_host, settingsLocation: settingsLocation);
        _packageManager = new(_settings);
        _constraintManager = new(_settings);
        _creator = new(_settings);
    }

    public TemplateEngineContext Context { get; }

    public bool ProjectTemplatesEnabled => _projectSupport is not null;

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

    internal NativeTemplatePackagePin? VerifiedNativePackage(ITemplatePackage? package)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return PackageInfo(package) is { } info
            && _packageScope?.ReceiptBoundPin(info) is { } pin
            && _packagePins.TryGetValue(package!.MountPointUri, out string? verified)
            && string.Equals(verified, pin.Sha256, StringComparison.OrdinalIgnoreCase) ? pin : null;
    }

    private static TemplatePackageInfo? PackageInfo(ITemplatePackage? package) =>
        package is IManagedTemplatePackage managed ? new(managed.Identifier, managed.Version) : null;

    public IReadOnlyList<TemplateProjectConfigurationDeclaration> ValidateProjectConfiguration(string configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_projectSupport is null)
        {
            throw new InvalidTemplateMetadataException("Mandatory project configuration is not enabled in this template flow.");
        }

        return ProjectSupport(configuration)?.DotNetLayout is { } layout
            ? _projectSupport.Planner.ReadDotNetLayout(configuration, layout)
            : _projectSupport.Planner.Read(configuration);
    }

    internal async Task QualifyProjectConfigurationAsync(ITemplateInfo template, TemplateCatalogEntry entry,
        TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(configuration);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_projectSupport?.QualifyLayoutAsync is not { } qualify)
        {
            return;
        }

        NativeProjectTemplateSource source = new(entry, configuration, $"{template.MountPointUri}::{template.ConfigPlace}",
            (path, token) => ReadPackageFileAsync(template, path, token));
        DotNetProjectLayoutQualification? layout = await qualify(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (layout is not null)
        {
            if (layout.TemplateIdentity != entry.Identity
                || !string.Equals(layout.ConfigurationFingerprint, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidTemplateMetadataException("Project layout qualification does not match the exact mounted candidate.");
            }

            _projectLayouts[configuration.Content] = layout;
        }
    }

    private TemplateProjectSupport? ProjectSupport(string configuration) => _projectSupport is { } support
        && _projectLayouts.TryGetValue(configuration, out DotNetProjectLayoutQualification? layout)
        ? support with { DotNetLayout = layout } : _projectSupport;

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

    internal async Task<NativeTemplatePreparationResult> CaptureNativePreparationAsync(ITemplateInfo template,
        TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request, Common.IFileSystem fileSystem,
        bool expandFiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult? invalid = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (invalid is not null)
        {
            return new(null, invalid);
        }

        invalid = await ValidateSourceSnapshotAsync(template, configuration, request.SourceSnapshot, cancellationToken);
        if (invalid is not null)
        {
            return new(null, invalid);
        }

        string? diagnostic = QualifyMsBuildBindings(template, configuration, request)
            ?? TemplateOutputPathValidator.GetDiagnostic(configuration.Content, GetPopulatedParameters(template, configuration, request), Context);
        if (diagnostic is not null)
        {
            return Failed(diagnostic);
        }

        ITemplateCreationResult preview = await InstantiateAsync(template, request, CreateInputData(configuration.Parameters, request.Parameters), true, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (preview.Status != CreationResultStatus.Success || preview.CreationEffects is null)
        {
            return Failed(preview.ErrorMessage ?? "Native preparation did not report evaluated creation effects.");
        }

        ICreationEffects effects = preview.CreationEffects;
        using var sourceConfiguration = new MemoryStream(Encoding.UTF8.GetBytes(configuration.Content));
        string? sourcePlaceholder = TemplateConfigModel.FromStream(sourceConfiguration).PlaceholderFilename;
        IReadOnlyList<string> sourcePlaceholders = string.IsNullOrWhiteSpace(sourcePlaceholder) ? ["-.-", "_._"] : [sourcePlaceholder];
        if (request.SourceSnapshot is { } snapshot && (!MatchesOutputSources(snapshot, effects.FileChanges)
            || effects.FileChanges.OfType<IFileChange2>().Any(change => sourcePlaceholders.Contains(Path.GetFileName(change.SourceRelativePath), StringComparer.Ordinal))))
        {
            return Failed("Native preparation does not match the qualified regular-file sources.");
        }

        TemplateFileChange[] changes = [.. effects.FileChanges.Select(change => TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind))];
        if (configuration.NativeGeneration is not null && configuration.Type == TemplateType.Item
            && NativeTemplateGenerationContract.ItemOutputDiagnostic(changes) is { } configurationOutput)
        {
            return Failed(configurationOutput);
        }
        bool cleanupPreview = IsCleanupPreview(template, request);
        diagnostic = ValidateOutputPaths(request.OutputDirectory.Info.FullName, changes, cleanupPreview);
        if (diagnostic is not null)
        {
            return Failed(diagnostic);
        }

        Dictionary<string, ReadOnlyMemory<byte>> expanded = new(StringComparer.Ordinal);
        if (expandFiles)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || effects.FileChanges.Any(change => change.ChangeKind != ChangeKind.Create
                && !(cleanupPreview && change.ChangeKind is ChangeKind.Change or ChangeKind.Overwrite)
                && !IsForcedItemReplacement(change)))
            {
                return Failed("Private native expansion requires an explicit effective name and create-only outputs or explicitly forced regular item files.");
            }

            using Common.TempDirectory temporary = fileSystem.CreateTempDirectory();
            var output = Common.WorkingDirectory.FromExplicit(temporary.Path);
            TemplateInvocationRequest expansionRequest = request with
            {
                OutputDirectory = output,
                EffectSupport = null,
                MsBuildBindings = null,
                Force = false,
                PreserveExistingFiles = false,
                PreviewAfterDirectoryCleanup = false,
            };
            diagnostic = ValidateOutputPaths(output.Info.FullName, changes);
            if (diagnostic is not null)
            {
                return Failed(diagnostic);
            }

            // Native instantiation reports post-actions but does not dispatch them.
            invalid = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken)
                ?? await ValidateSourceSnapshotAsync(template, configuration, request.SourceSnapshot, cancellationToken);
            if (invalid is not null)
            {
                return new(null, invalid);
            }

            ITemplateCreationResult created = await InstantiateAsync(template, expansionRequest, CreateInputData(configuration.Parameters, expansionRequest.Parameters), false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var configurationStream = new MemoryStream(Encoding.UTF8.GetBytes(configuration.Content));
            string? placeholder = TemplateConfigModel.FromStream(configurationStream).PlaceholderFilename;
            IReadOnlyList<string> placeholders = string.IsNullOrWhiteSpace(placeholder) ? ["-.-", "_._"] : [placeholder];
            HashSet<string> directories = new(effects.FileChanges.OfType<IFileChange2>()
                .Where(change => placeholders.Contains(Path.GetFileName(change.SourceRelativePath), StringComparer.Ordinal))
                .Select(change => change.TargetRelativePath), StringComparer.Ordinal);
            if (created.Status != CreationResultStatus.Success || created.CreationEffects is null
                || !created.CreationEffects.FileChanges.Select(change => change.TargetRelativePath).Order(StringComparer.Ordinal)
                    .SequenceEqual(effects.FileChanges.Select(change => change.TargetRelativePath).Order(StringComparer.Ordinal), StringComparer.Ordinal)
                || created.CreationEffects.FileChanges.Any(change => change.ChangeKind != ChangeKind.Create)
                || (request.SourceSnapshot is { } expandedSources && !MatchesOutputSources(expandedSources, created.CreationEffects.FileChanges))
                || ValidateOutputPaths(output.Info.FullName, changes) is not null
                || changes.Any(change => !HasExpectedOutputKind(output.Info.FullName, change, directories)))
            {
                return Failed("Private native expansion did not produce the exact previewed source output paths.");
            }

            foreach (IFileChange change in created.CreationEffects.FileChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(Path.Combine(output.Info.FullName, change.TargetRelativePath));
                if (fileSystem.FileExists(path))
                {
                    using Stream stream = _settings.Host.FileSystem.OpenRead(path);
                    using MemoryStream content = new();
                    await stream.CopyToAsync(content, cancellationToken);
                    expanded.Add(change.TargetRelativePath, content.ToArray());
                }
                else if (!fileSystem.DirectoryExists(path))
                {
                    return Failed("Private native expansion contains a missing or unsupported output kind.");
                }
            }

            StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            string[] expectedFiles = [.. expanded.Keys.Select(path => Path.GetFullPath(Path.Combine(output.Info.FullName, path))).Order(comparer)];
            if (!fileSystem.GetFiles(output.Info.FullName).Select(Path.GetFullPath).Order(comparer).SequenceEqual(expectedFiles, comparer))
            {
                return Failed("Private native expansion produced unexpected source files.");
            }

            HashSet<string> allowed = new(comparer);
            foreach (TemplateFileChange change in changes)
            {
                for (string? path = Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path));
                    path is not null && !comparer.Equals(path, output.Info.FullName); path = Path.GetDirectoryName(path))
                {
                    allowed.Add(path);
                }
            }

            Queue<string> inspect = new([output.Info.FullName]);
            while (inspect.TryDequeue(out string? directory))
            {
                foreach (string entry in fileSystem.GetFileSystemEntries(directory))
                {
                    if (!allowed.Contains(Path.GetFullPath(entry)))
                    {
                        return Failed("Private native expansion produced unexpected source entries.");
                    }

                    if (fileSystem.DirectoryExists(entry))
                    {
                        inspect.Enqueue(entry);
                    }
                }
            }
        }

        invalid = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return invalid is null ? new(new(new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint),
            effects, new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(expanded)), null) : new(null, invalid);

        static NativeTemplatePreparationResult Failed(string message) =>
            new(null, new(TemplateInvocationStatus.InvalidTemplate, [], message));

        bool IsForcedItemReplacement(IFileChange change)
        {
            string path = Path.GetFullPath(Path.Combine(request.OutputDirectory.Info.FullName, change.TargetRelativePath));
            return request.Force && configuration.Type == TemplateType.Item
                && change.ChangeKind is ChangeKind.Change or ChangeKind.Overwrite
                && change is IFileChange2 source && !sourcePlaceholders.Contains(Path.GetFileName(source.SourceRelativePath), StringComparer.Ordinal)
                && _host.FileSystem.FileExists(path) && !_host.FileSystem.DirectoryExists(path);
        }
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
        catch (IOException exception)
        {
            throw new InvalidTemplateMetadataException("Installed package host metadata could not be read.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidTemplateMetadataException("Installed package host metadata could not be read.", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidTemplateMetadataException("Installed package host metadata is invalid.", exception);
        }
        catch (PackagingException exception)
        {
            throw new InvalidTemplateMetadataException("Installed package host metadata is invalid.", exception);
        }
        catch (XmlException exception)
        {
            throw new InvalidTemplateMetadataException("Installed package host metadata is invalid.", exception);
        }
    }

    public async Task<TemplateConstraintEvaluation> EvaluateConstraintsAsync(string configuration, CancellationToken cancellationToken, IReadOnlyCollection<string>? packageTypes = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TemplateConstraintDeclaration> declarations = TemplateConstraintDeclarationReader.Read(configuration, packageTypes);
        using var document = JsonDocument.Parse(configuration, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject().Any(property =>
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

    public async Task<TemplateInvocationResult> InvokeAsync(ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult? configurationFailure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        TemplateInvocationResult? sourceFailure = await ValidateSourceSnapshotAsync(template, configuration, request.SourceSnapshot, cancellationToken);
        if (sourceFailure is not null)
        {
            return sourceFailure;
        }

        string? bindingFailure = QualifyMsBuildBindings(template, configuration, request);
        if (bindingFailure is not null)
        {
            return new(TemplateInvocationStatus.InvalidArguments, [], bindingFailure);
        }

        if (request.EffectSupport?.FSharpRepair is not null
            && (configuration.Type != TemplateType.Item
                || !template.TagsCollection.TryGetValue("language", out string? language) || language != "F#"
                || !string.Equals(Context.Project?.Stack, "dotnet", StringComparison.OrdinalIgnoreCase)
                || Context.Project?.Language != "F#"
                || request.MsBuildBindings?.TargetFramework != "net10.0"
                || Path.GetExtension(request.EffectSupport.ExactProjectPath) != ".fsproj"
                || (request.EffectSupport.FSharpRepair.GeneratedFiles.Any(file => file.ExpectedTargetSha256 is not null)
                    && (!request.Force || request.SourceSnapshot is null))))
        {
            return new(TemplateInvocationStatus.InvalidArguments, [],
                "F# compile repair requires the selected F# item, an exact evaluated net10.0 .fsproj snapshot and its controlled dependency capability.");
        }

        string? topologyFailure = TemplateOutputPathValidator.GetDiagnostic(configuration.Content, GetPopulatedParameters(template, configuration, request), Context);
        if (topologyFailure is not null)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], topologyFailure);
        }

        using var document = JsonDocument.Parse(configuration.Content, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        TemplateProjectSupport? projectSupport = configuration.Type == TemplateType.Project ? ProjectSupport(configuration.Content) : null;
        if (request.PreserveExistingFiles && projectSupport is null)
        {
            return new(TemplateInvocationStatus.InvalidArguments, [], "Existing-file preservation is only supported for project initialization.");
        }
        if (request.PreviewAfterDirectoryCleanup && (!request.DryRun || !request.Force || projectSupport is null))
        {
            return new(TemplateInvocationStatus.InvalidArguments, [],
                "Directory-cleanup intent is only supported for a forced project preview, never actual invocation.");
        }
        bool qualifiedDotNetProject = projectSupport?.DotNetLayout is not null;
        if (projectSupport is not null)
        {
            try
            {
                ValidateProjectConfiguration(configuration.Content);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message);
            }

            if (qualifiedDotNetProject && request.EffectSupport is null)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], "Qualified .NET project creation requires an explicit controlled dependency capability.");
            }
        }
        else if ((configuration.Type != TemplateType.Item || request.EffectSupport is null)
            && document.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, "postActions", StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind != JsonValueKind.Null && (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 0)))
        {
            return new(TemplateInvocationStatus.Failed, [], "Item post-actions require a qualified dispatcher and are not supported by this invocation path.");
        }

        Dictionary<string, string?> parameters = new(request.Parameters, StringComparer.Ordinal);
        string output = request.OutputDirectory.Info.FullName;
        ITemplateCreationResult preview = await InstantiateAsync(template, request, CreateInputData(configuration.Parameters, parameters), true, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (preview.Status != CreationResultStatus.Success)
        {
            return new(MapCreationStatus(preview.Status, cancellationToken), [], preview.ErrorMessage ?? preview.Status.ToString());
        }

        IReadOnlyList<TemplateFileChange> templateChanges = Array.AsReadOnly<TemplateFileChange>(
            [.. (preview.CreationEffects?.FileChanges ?? []).Select(change => TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind))]);
        if (configuration.NativeGeneration is not null && configuration.Type == TemplateType.Item
            && NativeTemplateGenerationContract.ItemOutputDiagnostic(templateChanges) is { } configurationOutput)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], configurationOutput);
        }
        HashSet<string> preserved = new(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (request.PreserveExistingFiles)
        {
            preserved.UnionWith(templateChanges.Select(change => Path.GetFullPath(Path.Combine(output, change.Path)))
                .Where(_settings.Host.FileSystem.FileExists));
            templateChanges = Array.AsReadOnly<TemplateFileChange>([.. templateChanges.Select(change =>
                preserved.Contains(Path.GetFullPath(Path.Combine(output, change.Path))) ? change with { Kind = TemplateFileChangeKind.Preserve } : change)]);
        }
        using var configurationStream = new MemoryStream(Encoding.UTF8.GetBytes(configuration.Content));
        string? placeholder = TemplateConfigModel.FromStream(configurationStream).PlaceholderFilename;
        IReadOnlyList<string> placeholderFilenames = string.IsNullOrWhiteSpace(placeholder) ? ["-.-", "_._"] : [placeholder];
        // Native placeholder source files represent directory effects, including when the directory is empty.
        HashSet<string> directoryOutputs = new((preview.CreationEffects?.FileChanges ?? []).OfType<IFileChange2>()
            .Where(change => placeholderFilenames.Contains(Path.GetFileName(change.SourceRelativePath), StringComparer.Ordinal))
            .Select(change => change.TargetRelativePath), StringComparer.Ordinal);
        if (request.SourceSnapshot is { } sourceSnapshot
            && (directoryOutputs.Count > 0 || !MatchesOutputSources(sourceSnapshot, preview.CreationEffects?.FileChanges ?? [])))
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], "Owned source qualification requires exactly the selected regular-file sources, not placeholder directories or extra outputs.");
        }

        IReadOnlyList<TemplateFileChange> changes = templateChanges;
        NativeTemplateEffectResult? effectPreview = null;
        IReadOnlyList<TemplateProjectConfigurationPlan> configurationPlans = [];
        if (projectSupport is not null)
        {
            try
            {
                if (preview.CreationEffects is null)
                {
                    return new(TemplateInvocationStatus.InvalidTemplate, [], "Project preview did not produce a configuration plan.");
                }

                configurationPlans = projectSupport.DotNetLayout is { } layout
                    ? projectSupport.Planner.CreateDotNetPlans(configuration.Content, preview.CreationEffects, output, layout, parameters, directoryOutputs)
                    : projectSupport.Planner.CreatePlans(configuration.Content, preview.CreationEffects, output, directoryOutputs);
                if (!request.PreviewAfterDirectoryCleanup && configurationPlans.Any(projectSupport.Writer.ConfigurationExists))
                {
                    return new(TemplateInvocationStatus.FileConflict, changes, TemplateProjectConfigurationWriter.ExistingConfigurationDiagnostic)
                    {
                        ProjectConfigurations = configurationPlans,
                    };
                }

                changes = Array.AsReadOnly<TemplateFileChange>([.. changes,
                    .. configurationPlans.Select(plan => new TemplateFileChange(Path.GetRelativePath(output, plan.ConfigurationPath), TemplateFileChangeKind.Create))]);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message)
                {
                    ProjectConfigurations = configurationPlans,
                };
            }
        }

        if ((configuration.Type == TemplateType.Item || qualifiedDotNetProject) && request.EffectSupport is { } effectSupport)
        {
            ArgumentNullException.ThrowIfNull(effectSupport.ProjectDirectory);
            ArgumentNullException.ThrowIfNull(effectSupport.Capability);
            if (preview.CreationEffects is null)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], "Native preview did not report evaluated creation effects.");
            }

            if (qualifiedDotNetProject)
            {
                StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                if (!Path.IsPathFullyQualified(effectSupport.ExactProjectPath)
                    || !comparer.Equals(Path.GetFullPath(effectSupport.ExactProjectPath), configurationPlans.Single().PrimaryOutput)
                    || !comparer.Equals(Path.TrimEndingDirectorySeparator(effectSupport.ProjectDirectory.Info.FullName),
                        Path.TrimEndingDirectorySeparator(output)))
                {
                    return new(TemplateInvocationStatus.EffectFailure, [],
                        "Qualified .NET dependency effects must target the native-generated project in the exact invocation root.")
                    {
                        ProjectConfigurations = configurationPlans,
                    };
                }
            }

            TemplateEffectSource selectedSource = new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint);
            NativeTemplateEffectPlanner planner = new();
            NativeTemplateEffectPlanningResult planned = effectSupport.FSharpRepair is { } repair
                ? planner.PlanFSharpItemDependencies(effectSupport.ProjectDirectory, selectedSource, preview.CreationEffects,
                    request.OutputDirectory, effectSupport.ExactProjectPath, repair, request.Force)
                : planner.PlanDependencies(effectSupport.ProjectDirectory, selectedSource, preview.CreationEffects.CreationResult,
                    qualifiedDotNetProject ? configurationPlans.Single().PrimaryOutput : effectSupport.ExactProjectPath);
            if (planned.Plan is null)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], string.Join(" ", planned.Diagnostics));
            }

            effectPreview = await new NativeTemplateEffectWorkflow().ExecuteAsync(planned.Plan,
                effectSupport.Capability, dryRun: true, confirmed: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!effectPreview.Succeeded)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], string.Join(" ", effectPreview.Diagnostics)) { Effects = effectPreview };
            }

            if (!request.DryRun && !effectSupport.Confirmed)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], "The controlled effect plan was not confirmed; no native files or effects were applied.")
                {
                    Effects = effectPreview with { Kind = "declined", Succeeded = false },
                };
            }
        }

        bool requiresForce = !request.Force && changes.Any(change => change.Kind != TemplateFileChangeKind.Create
            && !(request.PreserveExistingFiles && change.Kind == TemplateFileChangeKind.Preserve));
        if (!request.DryRun && requiresForce)
        {
            return new(TemplateInvocationStatus.FileConflict, changes, "Template would change existing files. Explicit file-conflict force is required.")
            {
                ProjectConfigurations = configurationPlans,
                Effects = effectPreview,
            };
        }

        string? outputFailure = ValidateOutputPaths(output, changes, request.PreviewAfterDirectoryCleanup);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure) { ProjectConfigurations = configurationPlans };
        }

        if (request.DryRun)
        {
            if (request.EffectSupport?.FSharpRepair is { } previewRepair
                && await ValidateFSharpTargetsAsync(previewRepair, generated: false, cancellationToken) is { } targetFailure)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], targetFailure);
            }
            return new(TemplateInvocationStatus.Success, changes, requiresForce ? "Actual creation requires explicit file-conflict force." : null)
            {
                ProjectConfigurations = configurationPlans,
                Effects = effectPreview,
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        configurationFailure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure with { ProjectConfigurations = configurationPlans };
        }

        sourceFailure = await ValidateSourceSnapshotAsync(template, configuration, request.SourceSnapshot, cancellationToken);
        if (sourceFailure is not null)
        {
            return sourceFailure with { ProjectConfigurations = configurationPlans };
        }

        outputFailure = ValidateOutputPaths(output, changes);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure) { ProjectConfigurations = configurationPlans };
        }
        if (request.EffectSupport?.FSharpRepair is { } pendingRepair)
        {
            if (await ValidateFSharpTargetsAsync(pendingRepair, generated: false, cancellationToken) is { } targetFailure)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], targetFailure);
            }
        }

        ITemplateCreationResult created;
        try
        {
            if (request.PreserveExistingFiles)
            {
                _host.InvocationFileSystem = new PreservingTemplateFileSystem(_host.FileSystem, output, preserved);
            }

            created = await InstantiateAsync(template, request with { Force = request.Force || request.PreserveExistingFiles },
                CreateInputData(configuration.Parameters, parameters), false, cancellationToken);
        }
        finally
        {
            _host.InvocationFileSystem = null;
        }
        if (created.Status != CreationResultStatus.Success)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(MapCreationStatus(created.Status, cancellationToken), [], created.ErrorMessage ?? created.Status.ToString())
            {
                ProjectConfigurations = configurationPlans,
            };
        }

        // Native success may already have committed files; preserve its effects even if cancellation arrives afterward.
        if (created.CreationEffects is null)
        {
            return new(TemplateInvocationStatus.EngineFailure, [], "The template engine did not report creation effects.")
            {
                ProjectConfigurations = configurationPlans,
            };
        }

        IReadOnlyList<TemplateFileChange> written = Array.AsReadOnly<TemplateFileChange>(
            [.. created.CreationEffects.FileChanges.Select(change => TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind))
                .Select(change => preserved.Contains(Path.GetFullPath(Path.Combine(output, change.Path)))
                    ? change with { Kind = TemplateFileChangeKind.Preserve } : change)]);
        if (!written.Select(change => change.Path).Order(StringComparer.Ordinal).SequenceEqual(templateChanges.Select(change => change.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || ValidateOutputPaths(output, templateChanges) is not null
            || (request.SourceSnapshot is { } writtenSources && !MatchesOutputSources(writtenSources, created.CreationEffects.FileChanges))
            || written.Any(change => !HasExpectedOutputKind(output, change, directoryOutputs)))
        {
            return new(TemplateInvocationStatus.EngineFailure, [], "Template creation did not produce the validated output paths.")
            {
                ProjectConfigurations = configurationPlans,
            };
        }

        if (projectSupport is null)
        {
            if (request.EffectSupport?.FSharpRepair is { } appliedRepair
                && await ValidateFSharpTargetsAsync(appliedRepair, generated: true, cancellationToken) is { } generatedFailure)
            {
                return new(TemplateInvocationStatus.EffectFailure, written, generatedFailure);
            }
            if (effectPreview is not null && request.EffectSupport is { } effects)
            {
                NativeTemplateEffectResult applied = await new NativeTemplateEffectWorkflow().ExecuteAsync(
                    effectPreview.Plan, effects.Capability, dryRun: false, confirmed: effects.Confirmed, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new(applied.Succeeded ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EffectFailure, written,
                    applied.Succeeded ? null : "Native template files were generated, but controlled effects did not complete. "
                        + string.Join(" ", applied.Diagnostics))
                {
                    Effects = applied,
                };
            }

            return new(TemplateInvocationStatus.Success, written);
        }

        List<TemplateFileChange> completedChanges = [.. written];
        List<string> configuredProjects = [];
        foreach (TemplateProjectConfigurationPlan plan in configurationPlans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outputFailure = ValidateOutputPaths(output, changes);
            if (outputFailure is not null)
            {
                return new(TemplateInvocationStatus.ConfigurationFailure, completedChanges.AsReadOnly(), outputFailure)
                {
                    ProjectConfigurations = configurationPlans,
                    ConfiguredProjects = configuredProjects.AsReadOnly(),
                };
            }

            try
            {
                await projectSupport.Writer.WriteAsync(plan, cancellationToken);
            }
            catch (IOException exception)
            {
                return ConfigurationFailure(exception, completedChanges.AsReadOnly(), configuredProjects, cancellationToken) with { ProjectConfigurations = configurationPlans };
            }
            catch (UnauthorizedAccessException exception)
            {
                return ConfigurationFailure(exception, completedChanges.AsReadOnly(), configuredProjects, cancellationToken) with { ProjectConfigurations = configurationPlans };
            }
            catch (InvalidOperationException exception)
            {
                return ConfigurationFailure(exception, completedChanges.AsReadOnly(), configuredProjects, cancellationToken) with { ProjectConfigurations = configurationPlans };
            }

            configuredProjects.Add(plan.Root);
            completedChanges.Add(new(Path.GetRelativePath(output, plan.ConfigurationPath), TemplateFileChangeKind.Create));
        }

        if (qualifiedDotNetProject && effectPreview is not null && request.EffectSupport is { } projectEffects)
        {
            outputFailure = ValidateOutputPaths(output, changes);
            if (outputFailure is not null || written.Any(change => !HasExpectedOutputKind(output, change, directoryOutputs)))
            {
                string diagnostic = outputFailure ?? "Native project outputs changed before dependency execution.";
                return new(TemplateInvocationStatus.EffectFailure, completedChanges.AsReadOnly(), diagnostic)
                {
                    ProjectConfigurations = configurationPlans,
                    ConfiguredProjects = configuredProjects.AsReadOnly(),
                    Effects = effectPreview with { Kind = "preflightFailed", Succeeded = false, Diagnostics = [diagnostic] },
                };
            }

            NativeTemplateEffectResult applied = await new NativeTemplateEffectWorkflow().ExecuteAsync(
                effectPreview.Plan, projectEffects.Capability, dryRun: false, confirmed: projectEffects.Confirmed, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(applied.Succeeded ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EffectFailure,
                completedChanges.AsReadOnly(), applied.Succeeded ? null
                    : "Native project files and CLI configuration were generated, but controlled dependencies did not complete. "
                        + string.Join(" ", applied.Diagnostics))
            {
                ProjectConfigurations = configurationPlans,
                ConfiguredProjects = configuredProjects.AsReadOnly(),
                Effects = applied,
            };
        }

        return new(TemplateInvocationStatus.Success, completedChanges.AsReadOnly())
        {
            ProjectConfigurations = configurationPlans,
            ConfiguredProjects = configuredProjects.AsReadOnly(),
        };
    }

    private async Task<string?> ValidateFSharpTargetsAsync(TemplateFSharpCompileEffect repair, bool generated, CancellationToken cancellationToken)
    {
        try
        {
            foreach (TemplateGeneratedFSharpFile file in repair.GeneratedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? expected = generated ? file.Sha256 : file.ExpectedTargetSha256;
                if (expected is null)
                {
                    if (_host.FileSystem.FileExists(file.Path) || _host.FileSystem.DirectoryExists(file.Path))
                    {
                        return "An F# native output became occupied after preparation; rerun the command.";
                    }
                    continue;
                }
                if (!_host.FileSystem.FileExists(file.Path) || _host.FileSystem.DirectoryExists(file.Path))
                {
                    return "The qualified F# native target is no longer a regular file; rerun the command.";
                }
                if ((_host.FileSystem.GetFileAttributes(file.Path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                {
                    return "The qualified F# native target became linked or occupied; rerun the command.";
                }
                using Stream stream = _host.FileSystem.OpenRead(file.Path);
                if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)),
                    expected, StringComparison.OrdinalIgnoreCase))
                {
                    return generated ? "The native-generated F# source does not match its qualified rendered hash."
                        : "The selected F# replacement target changed after preparation; rerun the command.";
                }
            }
            if ((_host.FileSystem.GetFileAttributes(repair.ProjectPath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                return "The selected F# project became linked or occupied; rerun the command.";
            }
            using Stream project = _host.FileSystem.OpenRead(repair.ProjectPath);
            return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(project, cancellationToken)),
                repair.ExpectedProjectSha256, StringComparison.OrdinalIgnoreCase) ? null
                    : "The selected F# project changed after preparation; rerun the command.";
        }
        catch (IOException exception)
        {
            return $"The qualified F# native target or project could not be read: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            return $"The qualified F# native target or project could not be read: {exception.Message}";
        }
    }

    private static TemplateInvocationResult ConfigurationFailure(Exception exception, IReadOnlyList<TemplateFileChange> changes,
        List<string> configuredProjects, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(TemplateInvocationStatus.ConfigurationFailure, changes, $"Mandatory project configuration failed. Generated files were preserved: {exception.Message}")
        {
            ConfiguredProjects = configuredProjects.AsReadOnly(),
        };
    }

    private string? QualifyMsBuildBindings(ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request)
    {
        if (request.MsBuildBindings is not { } snapshot)
        {
            if (_msBuildBindings is not null)
            {
                return "This controlled session requires its exact explicit MSBuild snapshot on every invocation.";
            }

            if (configuration.Type == TemplateType.Item && request.EffectSupport is not null)
            {
                using var document = JsonDocument.Parse(configuration.Content,
                    new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (document.RootElement.TryGetProperty("symbols", out JsonElement symbols) && symbols.ValueKind == JsonValueKind.Object
                    && symbols.EnumerateObject().Any(symbol => symbol.Value.ValueKind == JsonValueKind.Object
                        && symbol.Value.TryGetProperty("binding", out JsonElement binding) && binding.ValueKind == JsonValueKind.String
                        && binding.GetString()!.StartsWith("msbuild:", StringComparison.OrdinalIgnoreCase)))
                {
                    return "Controlled item effects require an explicit evaluated MSBuild snapshot; template framework defaults are not a project binding.";
                }
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

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal async Task<TemplateInvocationResult?> ValidateSelectedConfigurationAsync(ITemplateInfo template, TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        TemplateConfigurationSnapshot current;
        try
        {
            if (_packagePins.TryGetValue(template.MountPointUri, out string? expected)
                && await ValidatePackageHashAsync(template.MountPointUri, expected, cancellationToken) is { } packageFailure)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], packageFailure);
            }
            current = await ReadConfigurationAsync(template, cancellationToken);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Selected template configuration is invalid. Rerun selection: {exception.Message}");
        }
        catch (IOException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Selected template configuration is unreadable. Rerun selection: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Selected template configuration is unreadable. Rerun selection: {exception.Message}");
        }

        if (!string.Equals(current.Fingerprint, configuration.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(current.HostConfigPlace, configuration.HostConfigPlace, StringComparison.Ordinal)
            || !string.Equals(current.HostData, configuration.HostData, StringComparison.Ordinal))
        {
            return new(TemplateInvocationStatus.TemplateChanged, [], "Selected template configuration changed. Rerun selection.");
        }

        bool qualified = current.Type == TemplateType.Item || (current.Type == TemplateType.Project && ProjectTemplatesEnabled);
        return qualified && current.Type == configuration.Type
            ? null
            : new(TemplateInvocationStatus.InvalidTemplate, [], ProjectTemplatesEnabled
                ? "Current mounted template type is not qualified for this invocation flow. Correct its type and rerun selection."
                : "Current mounted template type is not a qualified item template. Correct its type and rerun selection.");
    }

    private Dictionary<string, object> GetPopulatedParameters(ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request)
    {
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

    private static TemplateInvocationStatus MapCreationStatus(CreationResultStatus status, CancellationToken cancellationToken)
        => status switch
        {
            CreationResultStatus.Success => TemplateInvocationStatus.Success,
            CreationResultStatus.InvalidParamValues => TemplateInvocationStatus.InvalidArguments,
            CreationResultStatus.MissingMandatoryParam => TemplateInvocationStatus.MissingRequiredParameters,
            CreationResultStatus.DestructiveChangesDetected => TemplateInvocationStatus.FileConflict,
            CreationResultStatus.NotFound => TemplateInvocationStatus.TemplateUnavailable,
            CreationResultStatus.TemplateIssueDetected or CreationResultStatus.CondtionsEvaluationMismatch => TemplateInvocationStatus.InvalidTemplate,
            CreationResultStatus.Cancelled => throw new OperationCanceledException(cancellationToken),
            _ => TemplateInvocationStatus.EngineFailure,
        };

    private static InputDataSet CreateInputData(IReadOnlyList<ITemplateParameter> definitions, IReadOnlyDictionary<string, string?> parameters)
    {
        List<InputParameterData> input = [];
        foreach (ITemplateParameter definition in definitions)
        {
            bool supplied = parameters.TryGetValue(definition.Name, out string? value);
            input.Add(new(definition, value, supplied ? DataSource.User : DataSource.NoSource,
                supplied ? InputDataStateUtil.GetInputDataState(value) : InputDataState.Unset));
        }

        return new(new ParameterDefinitionSet(definitions), input);
    }

    protected virtual async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
        InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
    {
        IPhysicalFileSystem? previous = _host.InvocationFileSystem;
        if (dryRun && IsCleanupPreview(template, request))
        {
            _host.InvocationFileSystem = new PlannedCleanupTemplateFileSystem(_host.FileSystem, request.OutputDirectory.Info.FullName);
        }
        try
        {
            return await _creator.InstantiateAsync(template, request.Name, request.OutputDirectory.Info.Name, request.OutputDirectory.Info.FullName,
                parameters, forceCreation: dryRun || request.Force, dryRun: dryRun, cancellationToken: cancellationToken);
        }
        finally
        {
            _host.InvocationFileSystem = previous;
        }
    }

    private bool IsCleanupPreview(ITemplateInfo template, TemplateInvocationRequest request) =>
        request.PreviewAfterDirectoryCleanup && request.DryRun && request.Force && ProjectTemplatesEnabled
        && TemplateCatalogProjector.GetTemplateType(template) == TemplateType.Project;

    private bool HasExpectedOutputKind(string output, TemplateFileChange change, HashSet<string> directoryOutputs)
    {
        string path = Path.Combine(output, change.Path);
        bool file = _settings.Host.FileSystem.FileExists(path);
        bool directory = _settings.Host.FileSystem.DirectoryExists(path);
        if (change.Kind == TemplateFileChangeKind.Delete)
        {
            return !file && !directory;
        }

        return directoryOutputs.Contains(change.Path) ? directory && !file : file && !directory;
    }

    private async Task<TemplateInvocationResult?> ValidateSourceSnapshotAsync(ITemplateInfo template, TemplateConfigurationSnapshot configuration,
        NativeTemplateSourceSnapshot? snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null)
        {
            return null;
        }

        if (configuration.Type != TemplateType.Item || snapshot.TemplateIdentity != template.Identity
            || !string.Equals(snapshot.ConfigurationSha256, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], "Source qualification is not bound to the exact selected item configuration.");
        }

        foreach (TemplateEffectSource pin in snapshot.Sources)
        {
            ReadOnlyMemory<byte> source;
            try
            {
                source = await ReadPackageFileAsync(template, pin.Identity, cancellationToken);
            }
            catch (FileNotFoundException exception)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], exception.Message);
            }

            if (!string.Equals(Convert.ToHexString(SHA256.HashData(source.Span)), pin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], "A selected package source changed. Rerun selection.");
            }
        }

        return null;
    }

    private static bool MatchesOutputSources(NativeTemplateSourceSnapshot snapshot, IEnumerable<IFileChange> changes)
    {
        IFileChange[] files = [.. changes];
        return files.Length == snapshot.Outputs.Count
            && files.All(change => change.ChangeKind is ChangeKind.Create or ChangeKind.Change or ChangeKind.Overwrite
                && change is IFileChange2 source && snapshot.Outputs.Count(expected =>
                NativeTemplateSourceSnapshot.Normalize(expected.SourceRelativePath) == NativeTemplateSourceSnapshot.Normalize(source.SourceRelativePath)) == 1)
            && files.OfType<IFileChange2>().Select(file => NativeTemplateSourceSnapshot.Normalize(file.SourceRelativePath)).Distinct(StringComparer.Ordinal).Count() == files.Length
            && files.Select(file => file.TargetRelativePath).Distinct(StringComparer.Ordinal).Count() == files.Length;
    }

    private string? ValidateOutputPaths(string output, IReadOnlyList<TemplateFileChange> changes, bool cleanupPreview = false)
    {
        string boundary = output;
        PlannedCleanupTemplateFileSystem? planned = cleanupPreview ? new(_host.FileSystem, output) : null;
        foreach (TemplateFileChange change in changes)
        {
            string? path = Path.GetFullPath(Path.Combine(output, change.Path));
            string target = path;
            string relative = Path.GetRelativePath(output, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return "Template output escapes the requested output directory.";
            }
            if (planned?.IsPreservedPath(path) == true)
            {
                return "A force preview cannot write preserved .git content.";
            }

            // The requested output directory is the boundary; linked parents do not redirect individual template paths.
            while (path is not null && !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(boundary), StringComparison.Ordinal))
            {
                try
                {
                    FileAttributes attributes = _host.FileSystem.GetFileAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return "Template output uses a symbolic link or reparse point. Choose an unlinked output directory.";
                    }

                    if (!string.Equals(path, target, StringComparison.Ordinal) && (attributes & FileAttributes.Directory) == 0
                        && planned?.IsScheduledForDeletion(path) != true)
                    {
                        return "A template output directory ancestor is an existing file. Choose an output directory without file collisions.";
                    }
                }
                catch (FileNotFoundException)
                {
                    path = Path.GetDirectoryName(path);
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    path = Path.GetDirectoryName(path);
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    return "Template output path cannot be checked safely.";
                }
                catch (IOException)
                {
                    return "Template output path cannot be checked safely.";
                }

                path = Path.GetDirectoryName(path);
            }
        }

        return null;
    }

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
