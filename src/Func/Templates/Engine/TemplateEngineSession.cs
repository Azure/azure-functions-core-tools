// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
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
    private readonly TemplateProjectSupport? _projectSupport;
    private readonly IDisposable? _ownership;
    private readonly TemplateStoreEngineDiagnostics? _diagnostics;
    private bool _disposed;

    public TemplateEngineSession(TemplateEngineContext context, string settingsLocation, TemplateConstraintEvaluationSnapshot? snapshot = null,
        TemplateProjectSupport? projectSupport = null,
        IDisposable? ownership = null, TemplateStoreEngineDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);
        Context = context;
        _projectSupport = projectSupport;
        _ownership = ownership;
        _diagnostics = diagnostics;
        _settings = new(new FuncTemplateEngineHost(context, snapshot, diagnostics), settingsLocation: settingsLocation);
        _packageManager = new(_settings);
        _constraintManager = new(_settings);
        _creator = new(_settings);
    }

    public TemplateEngineContext Context { get; }

    public bool ProjectTemplatesEnabled => _projectSupport is not null;

    public IReadOnlyList<TemplateProjectConfigurationDeclaration> ValidateProjectConfiguration(string configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_projectSupport is null)
        {
            throw new InvalidTemplateMetadataException("Mandatory project configuration is not enabled in this template flow.");
        }

        return _projectSupport.Planner.Read(configuration);
    }

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

        string? topologyFailure = TemplateOutputPathValidator.GetDiagnostic(configuration.Content, GetPopulatedParameters(template, configuration, request));
        if (topologyFailure is not null)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], topologyFailure);
        }

        using var document = JsonDocument.Parse(configuration.Content, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        TemplateProjectSupport? projectSupport = configuration.Type == TemplateType.Project ? _projectSupport : null;
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
        }
        else if (document.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, "postActions", StringComparison.OrdinalIgnoreCase)
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
            [.. (preview.CreationEffects?.FileChanges ?? []).Select(change => new TemplateFileChange(change.TargetRelativePath, change.ChangeKind.ToString()))]);
        using var configurationStream = new MemoryStream(Encoding.UTF8.GetBytes(configuration.Content));
        string? placeholder = TemplateConfigModel.FromStream(configurationStream).PlaceholderFilename;
        IReadOnlyList<string> placeholderFilenames = string.IsNullOrWhiteSpace(placeholder) ? ["-.-", "_._"] : [placeholder];
        // Native placeholder source files represent directory effects, including when the directory is empty.
        HashSet<string> directoryOutputs = new((preview.CreationEffects?.FileChanges ?? []).OfType<IFileChange2>()
            .Where(change => placeholderFilenames.Contains(Path.GetFileName(change.SourceRelativePath), StringComparer.Ordinal))
            .Select(change => change.TargetRelativePath), StringComparer.Ordinal);
        IReadOnlyList<TemplateFileChange> changes = templateChanges;
        IReadOnlyList<TemplateProjectConfigurationPlan> configurationPlans = [];
        if (projectSupport is not null)
        {
            try
            {
                if (preview.CreationEffects is null)
                {
                    return new(TemplateInvocationStatus.InvalidTemplate, [], "Project preview did not produce a configuration plan.");
                }

                configurationPlans = projectSupport.Planner.CreatePlans(configuration.Content, preview.CreationEffects, output);
                if (configurationPlans.Any(projectSupport.Writer.ConfigurationExists))
                {
                    return new(TemplateInvocationStatus.FileConflict, changes, TemplateProjectConfigurationWriter.ExistingConfigurationDiagnostic)
                    {
                        ProjectConfigurations = configurationPlans,
                    };
                }

                changes = Array.AsReadOnly<TemplateFileChange>([.. changes,
                    .. configurationPlans.Select(plan => new TemplateFileChange(Path.GetRelativePath(output, plan.ConfigurationPath), "Create"))]);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message)
                {
                    ProjectConfigurations = configurationPlans,
                };
            }
        }

        bool requiresForce = !request.Force && changes.Any(change => !string.Equals(change.Kind, "Create", StringComparison.Ordinal));
        if (!request.DryRun && requiresForce)
        {
            return new(TemplateInvocationStatus.FileConflict, changes, "Template would change existing files. Explicit file-conflict force is required.")
            {
                ProjectConfigurations = configurationPlans,
            };
        }

        string? outputFailure = ValidateOutputPaths(output, changes);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure) { ProjectConfigurations = configurationPlans };
        }

        if (request.DryRun)
        {
            return new(TemplateInvocationStatus.Success, changes, requiresForce ? "Actual creation requires explicit file-conflict force." : null)
            {
                ProjectConfigurations = configurationPlans,
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        configurationFailure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure with { ProjectConfigurations = configurationPlans };
        }

        outputFailure = ValidateOutputPaths(output, changes);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure) { ProjectConfigurations = configurationPlans };
        }

        ITemplateCreationResult created = await InstantiateAsync(template, request, CreateInputData(configuration.Parameters, parameters), false, cancellationToken);
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
            [.. created.CreationEffects.FileChanges.Select(change => new TemplateFileChange(change.TargetRelativePath, change.ChangeKind.ToString()))]);
        if (!written.Select(change => change.Path).Order(StringComparer.Ordinal).SequenceEqual(templateChanges.Select(change => change.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || ValidateOutputPaths(output, templateChanges) is not null
            || written.Any(change => !HasExpectedOutputKind(output, change, directoryOutputs)))
        {
            return new(TemplateInvocationStatus.EngineFailure, [], "Template creation did not produce the validated output paths.")
            {
                ProjectConfigurations = configurationPlans,
            };
        }

        if (projectSupport is null)
        {
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
            completedChanges.Add(new(Path.GetRelativePath(output, plan.ConfigurationPath), "Create"));
        }

        return new(TemplateInvocationStatus.Success, completedChanges.AsReadOnly())
        {
            ProjectConfigurations = configurationPlans,
            ConfiguredProjects = configuredProjects.AsReadOnly(),
        };
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

    private async Task<TemplateInvocationResult?> ValidateSelectedConfigurationAsync(ITemplateInfo template, TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        TemplateConfigurationSnapshot current;
        try
        {
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

    protected virtual Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        => _creator.InstantiateAsync(template, request.Name, request.OutputDirectory.Info.Name, request.OutputDirectory.Info.FullName, parameters,
            forceCreation: dryRun || request.Force, dryRun: dryRun, cancellationToken: cancellationToken);

    private bool HasExpectedOutputKind(string output, TemplateFileChange change, HashSet<string> directoryOutputs)
    {
        string path = Path.Combine(output, change.Path);
        bool file = _settings.Host.FileSystem.FileExists(path);
        bool directory = _settings.Host.FileSystem.DirectoryExists(path);
        if (string.Equals(change.Kind, "Delete", StringComparison.Ordinal))
        {
            return !file && !directory;
        }

        return directoryOutputs.Contains(change.Path) ? directory && !file : file && !directory;
    }

    private string? ValidateOutputPaths(string output, IReadOnlyList<TemplateFileChange> changes)
    {
        foreach (TemplateFileChange change in changes)
        {
            string? path = Path.GetFullPath(Path.Combine(output, change.Path));
            string target = path;
            string relative = Path.GetRelativePath(output, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return "Template output escapes the requested output directory.";
            }

            // The requested output directory is the boundary; linked parents do not redirect individual template paths.
            while (path is not null && !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(output), StringComparison.Ordinal))
            {
                try
                {
                    FileAttributes attributes = _settings.Host.FileSystem.GetFileAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return "Template output uses a symbolic link or reparse point. Choose an unlinked output directory.";
                    }

                    if (!string.Equals(path, target, StringComparison.Ordinal) && (attributes & FileAttributes.Directory) == 0)
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
