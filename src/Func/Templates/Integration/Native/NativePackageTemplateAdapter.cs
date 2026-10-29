// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Integration.Native;

/// <summary>
/// Registers one retained package in private engine state and captures its exact selected candidate's output.
/// </summary>
internal sealed class NativePackageTemplateAdapter(
    IFileSystem fileSystem, InstalledProjectStackCatalog stacks, TemplatePackageIntakeLimits limits) : IPackageTemplateAdapter
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly InstalledProjectStackCatalog _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly TemplatePackageIntakeLimits _limits = limits ?? throw new ArgumentNullException(nameof(limits));

    public async Task<PackageTemplateSession> OpenAsync(
        TemplatePackageContentLease package, TemplateEngineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!package.PackageTypes.Contains("Template", StringComparer.Ordinal)
            || !package.PackageTypes.Contains("FuncTemplate", StringComparer.Ordinal))
        {
            throw new InvalidTemplateMetadataException("The native package path requires authored Template and FuncTemplate package types.");
        }

        IReadOnlyDictionary<string, NativeTemplateMetadata> originals = await ReadOriginalsAsync(package, cancellationToken);
        string workspace = _fileSystem.CreateTempDirectory().Path;
        TemplateEngineSession? engine = null;
        bool transferred = false;
        Task<PackageTemplateSession> open = OpenCoreAsync();
        try
        {
            PackageTemplateSession result = await open;
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                Task cleanup = ReleaseOpeningResourcesAsync();
                try
                {
                    await cleanup;
                }
                finally
                {
                    if (!cleanup.IsCompletedSuccessfully)
                    {
                        List<Exception> failures = open.IsCanceled ? [new TaskCanceledException(open)] : [.. open.Exception!.InnerExceptions];
                        failures.AddRange(cleanup.IsCanceled ? [new TaskCanceledException(cleanup)] : cleanup.Exception!.InnerExceptions);
                        throw new AggregateException("Opening the native package and releasing its private state both failed.", failures);
                    }
                }
            }
        }

        // Keep synchronous release failures observable alongside the opening outcome.
        async Task ReleaseOpeningResourcesAsync() => await ReleaseAsync(engine, workspace);

        async Task<PackageTemplateSession> OpenCoreAsync()
        {
            string archivePath = Path.Combine(workspace, "retained.nupkg");
            await using (Stream retained = package.OpenRead())
            await using (Stream destination = _fileSystem.CreateNewFile(archivePath))
            {
                await retained.CopyToAsync(destination, cancellationToken);
            }

            engine = new(context, Path.Combine(workspace, "hive"));
            IManagedTemplatePackageProvider provider = engine.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            IReadOnlyList<InstallResult> installed = await provider.InstallAsync([new InstallRequest(archivePath)], cancellationToken);
            if (installed.Count != 1 || !installed[0].Success)
            {
                throw new InvalidTemplateMetadataException($"The retained package could not be registered privately: {string.Join("; ", installed.Select(result => result.ErrorMessage))}");
            }

            IReadOnlyList<ITemplateInfo> native = await engine.PackageManager.GetTemplatesAsync(cancellationToken);
            Dictionary<string, ITemplateInfo> byPath = new(StringComparer.Ordinal);
            foreach (ITemplateInfo template in native)
            {
                string key = template.ConfigPlace.Replace('\\', '/').TrimStart('/');
                if (!byPath.TryAdd(key, template))
                {
                    throw new InvalidTemplateMetadataException($"Native registration returned duplicate configuration binding '{key}'.");
                }
            }

            if (byPath.Count != originals.Count || originals.Keys.Any(path => !byPath.ContainsKey(path)))
            {
                throw new InvalidTemplateMetadataException("Native discovery did not preserve every validated original candidate. Correct the package metadata; no candidate will be substituted.");
            }

            List<PackageTemplateCandidate> candidates = [];
            foreach ((string path, NativeTemplateMetadata raw) in originals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ITemplateInfo template = byPath[path];
                await VerifyMountAsync(template, package.Sha256, workspace, cancellationToken);
                if (!string.Equals(template.Identity, raw.Identity, StringComparison.Ordinal)
                    || TemplateCatalogProjector.GetTemplateType(template) != raw.Type)
                {
                    throw new InvalidTemplateMetadataException($"{path}: native normalization changed the validated candidate identity or type.");
                }

                TemplateCatalogEntry projected = TemplateCatalogProjector.Project(template, null);
                IReadOnlyList<TemplateParameterDefinition> parameters = TemplateParameterProjector.Project(template.ParameterDefinitions, raw.HostMetadata);
                projected = projected with
                {
                    Package = new(package.PackageId, package.Version),
                    Parameters = [.. parameters.Select(parameter => raw.ParameterDefaults.TryGetValue(parameter.CanonicalName, out string? value)
                        ? parameter with { DefaultValue = value } : parameter)],
                    IsHidden = raw.HostMetadata.IsHidden,
                    DeclaredInternalRole = raw.DeclaredInternalRole,
                    DefaultName = raw.DefaultName,
                    MetadataDiagnostic = null,
                };
                TemplateDescriptor descriptor = new(projected, raw.RequirementDeclaration);
                if (raw.Configuration is { } intent)
                {
                    ValidateStack(intent);
                }

                (FuncTemplateConstraintOutcome eligibility, string[] diagnostics) = await EvaluateAsync(engine, raw, cancellationToken);
                candidates.Add(new NativeCandidate(this, descriptor, eligibility, diagnostics, template, raw, engine, context, workspace, package.Sha256));
            }

            return new NativeSession(candidates, () => ReleaseAsync(engine, workspace));
        }
    }

    private async Task<IReadOnlyDictionary<string, NativeTemplateMetadata>> ReadOriginalsAsync(
        TemplatePackageContentLease package, CancellationToken cancellationToken)
    {
        using Stream retained = package.OpenRead();
        using ZipArchive archive = new(retained, ZipArchiveMode.Read);
        Dictionary<string, NativeTemplateMetadata> originals = new(StringComparer.Ordinal);
        foreach (TemplatePackageInventoryEntry entry in package.Inventory.Where(entry =>
            entry.Kind == TemplatePackageEntryKind.File
            && (entry.Path == ".template.config/template.json" || entry.Path.EndsWith("/.template.config/template.json", StringComparison.Ordinal))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string configuration = await ReadMetadataAsync(archive.GetEntry(entry.Path)!, cancellationToken);
            string hostPath = entry.Path[..^"template.json".Length] + FuncHostTemplateMetadataReader.FileName;
            string? host = archive.GetEntry(hostPath) is { } hostEntry ? await ReadMetadataAsync(hostEntry, cancellationToken) : null;
            originals.Add(entry.Path, new(entry.Path, configuration, host));
        }

        if (originals.Count == 0)
        {
            throw new InvalidTemplateMetadataException("The retained package contains no authored .template.config/template.json candidates.");
        }

        return originals;
    }

    private async Task<string> ReadMetadataAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length > Math.Min(_limits.MaxEntryBytes, 1024 * 1024))
        {
            throw new InvalidTemplateMetadataException($"{entry.FullName}: native metadata exceeds the one-MiB per-file bound.");
        }

        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        try
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidTemplateMetadataException(
                $"{entry.FullName}: native metadata contains invalid encoded text. Correct the original package metadata bytes.", exception);
        }
    }

    private async Task VerifyMountAsync(ITemplateInfo template, string digest, string workspace, CancellationToken cancellationToken)
    {
        string mount = template.MountPointUri;
        if (!Path.IsPathFullyQualified(mount) || !IsWithin(workspace, mount)
            || !string.Equals(await _fileSystem.ComputeSha256Async(mount, cancellationToken), digest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidTemplateMetadataException("The selected native mount is not the exact retained package inside this private session.");
        }
    }

    private async Task<(FuncTemplateConstraintOutcome Outcome, string[] Diagnostics)> EvaluateAsync(
        TemplateEngineSession engine, NativeTemplateMetadata raw, CancellationToken cancellationToken)
    {
        using TemplateConstraintManager manager = new(engine.Settings);
        List<string> diagnostics = [];
        FuncTemplateConstraintOutcome outcome = FuncTemplateConstraintOutcome.Eligible;
        if (!raw.Constraints.Any(constraint => constraint.Type == "host"))
        {
            return (FuncTemplateConstraintOutcome.NotEvaluated, [$"{raw.Path}: declare native func host eligibility for this package profile."]);
        }

        foreach (TemplateConstraintDeclaration constraint in raw.Constraints)
        {
            TemplateConstraintResult result = await manager.EvaluateConstraintAsync(
                constraint.Type, constraint.Arguments?.GetRawText() ?? string.Empty, cancellationToken);
            if (result.EvaluationStatus != TemplateConstraintResult.Status.Allowed)
            {
                diagnostics.Add($"{raw.Path}: native {constraint.Type}: {result.EvaluationStatus}. {result.LocalizedErrorMessage} {result.CallToAction}");
                outcome = result.EvaluationStatus == TemplateConstraintResult.Status.Restricted
                    && outcome != FuncTemplateConstraintOutcome.NotEvaluated
                        ? FuncTemplateConstraintOutcome.Restricted : FuncTemplateConstraintOutcome.NotEvaluated;
            }
        }

        return (outcome, [.. diagnostics]);
    }

    private async Task<PackageTemplateCapture> RenderAsync(NativeCandidate candidate, TemplateArgumentParseResult arguments, string? name, CancellationToken token)
    {
        name ??= candidate.Raw.DefaultName;
        _ = candidate.Engine.Settings;
        await VerifyMountAsync(candidate.Template, candidate.Digest, candidate.Workspace, token);
        if (name is not null)
        {
            try
            {
                if (name.Contains('/') || name.Contains('\\'))
                {
                    throw new ArgumentException("The native name cannot contain a path separator.", nameof(name));
                }

                PreparedOutputEntry.NormalizeRelativePath(name);
            }
            catch (ArgumentException exception)
            {
                throw new TemplatePreparationException(TemplatePreparationFailure.InvalidNativeInput,
                    "Supply one safe template name, not a relative path or device name.", innerException: exception);
            }
        }

        string output = _fileSystem.CreateStagingDirectory(candidate.Workspace);
        TemplateCreator creator = new(candidate.Engine.Settings);
        string fallback = Path.GetFileName(Path.TrimEndingDirectorySeparator(candidate.Context.CommandDirectory.Info.FullName));
        InputDataSet parameters = CreateInput(candidate.Template, arguments);

        ITemplateCreationResult preview = await creator.InstantiateAsync(
            candidate.Template, name, fallback, output, parameters, forceCreation: false,
            baselineName: null, dryRun: true, cancellationToken: token);
        RequireSuccess(preview, candidate.Raw.Path);
        foreach (IFileChange change in preview.CreationEffects!.FileChanges)
        {
            try
            {
                NormalizeNativePath(change.TargetRelativePath);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidTemplateMetadataException(
                    $"{candidate.Raw.Path}: native output path '{change.TargetRelativePath}' is not a safe logical relative path.", exception);
            }
        }

        ITemplateCreationResult rendered = await creator.InstantiateAsync(
            candidate.Template, name, fallback, output, parameters, forceCreation: false,
            baselineName: null, dryRun: false, cancellationToken: token);
        RequireSuccess(rendered, candidate.Raw.Path);
        List<PreparedOutputEntry> entries = [];
        long total = 0;
        await CaptureDirectoryAsync(output);

        PreparedConfiguration? configuration = candidate.Raw.Configuration is { } intent
            ? PrepareConfiguration(intent, rendered.CreationResult!, entries)
            : null;
        return new(entries, configuration);

        async Task CaptureDirectoryAsync(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (!IsWithin(output, path) || _fileSystem.GetAttributesIfExists(path) is not { } attributes
                    || (attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                {
                    throw new TemplatePreparationException(TemplatePreparationFailure.CaptureRejected,
                        $"{candidate.Raw.Path}: native rendered path '{path}' is missing, outside private output, or unsafe for capture.");
                }

                string relative = Path.GetRelativePath(output, path).Replace('\\', '/');
                if (entries.Count >= _limits.MaxEntries || relative.Length > _limits.MaxPathLength)
                {
                    throw new TemplatePreparationException(TemplatePreparationFailure.CaptureRejected,
                        $"{candidate.Raw.Path}: captured output '{relative}' exceeds the entry count or path-length bound.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    int beforeChildren = entries.Count;
                    await CaptureDirectoryAsync(path);
                    if (entries.Count == beforeChildren)
                    {
                        entries.Add(new(relative, PreparedOutputKind.Directory));
                    }

                    continue;
                }

                await using Stream content = _fileSystem.OpenReadIfExists(path) ?? throw new IOException($"Rendered output '{relative}' disappeared.");
                if (content.Length > _limits.MaxEntryBytes || total + content.Length > Math.Min(_limits.MaxExpandedBytes, _limits.MaxBufferBytes / 2)
                    || content.Length > int.MaxValue)
                {
                    throw new TemplatePreparationException(TemplatePreparationFailure.CaptureRejected,
                        $"{candidate.Raw.Path}: captured output '{relative}' exceeds the capture byte limits.");
                }

                total += content.Length;
                byte[] bytes = new byte[(int)content.Length];
                await content.ReadExactlyAsync(bytes, token);
                entries.Add(new(relative, PreparedOutputKind.File, bytes));
            }
        }
    }

    private PreparedConfiguration PrepareConfiguration(
        NativeProjectConfiguration intent, ICreationResult result, IReadOnlyList<PreparedOutputEntry> entries)
    {
        ValidateStack(intent);
        if (result.PrimaryOutputs.Count != intent.AuthoredOutputCount || result.PostActions.Count != 1
            || result.PostActions[0].ActionId != NativeTemplateMetadata.ConfigurationActionId
            || result.PostActions[0].ContinueOnError
            || !result.PostActions[0].Args.TryGetValue("stack", out string? actualStack) || actualStack != intent.Stack
            || !result.PostActions[0].Args.TryGetValue("language", out string? actualLanguage) || actualLanguage != intent.Language
            || !result.PostActions[0].Args.TryGetValue("primaryOutputIndex", out string? actualIndex)
            || actualIndex != intent.PrimaryOutputIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            throw new InvalidTemplateMetadataException("Native configuration results no longer preserve the single authored configuration intent and primary-output index.");
        }

        string primary = NormalizeNativePath(result.PrimaryOutputs[intent.PrimaryOutputIndex].Path);
        if (!entries.Any(entry => entry.RelativePath == primary && entry.Kind == PreparedOutputKind.File))
        {
            throw new InvalidTemplateMetadataException("The authored primary output does not resolve to an actual captured regular file.");
        }

        int separator = primary.LastIndexOf('/');
        string root = separator < 0 ? "." : primary[..separator];
        string host = root == "." ? "host.json" : root + "/host.json";
        if (entries.Count(entry => entry.RelativePath == host && entry.Kind == PreparedOutputKind.File) != 1
            || entries.Count(entry => entry.RelativePath.Split('/')[^1].Equals("host.json", StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidTemplateMetadataException("Project capture requires exactly one independently verified Functions root containing a regular host.json.");
        }

        Dictionary<string, object> payload = new(StringComparer.Ordinal)
        {
            [CliConfigurationNames.StackSectionName] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CliConfigurationNames.StackRuntimeKey] = intent.Stack,
                [CliConfigurationNames.StackLanguageKey] = intent.Language,
            },
        };
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = true });
        return new(root, [.. serialized, (byte)'\n']);
    }

    private ValueTask ReleaseAsync(TemplateEngineSession? engine, string workspace)
    {
        List<Exception> failures = [];
        try
        {
            engine?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or AggregateException)
        {
            failures.Add(exception);
        }

        try
        {
            _fileSystem.DeleteDirectory(workspace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or AggregateException)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        else if (failures.Count > 1)
        {
            throw new AggregateException("Native session disposal and private workspace cleanup both failed.", failures);
        }

        return ValueTask.CompletedTask;
    }

    private void ValidateStack(NativeProjectConfiguration intent)
    {
        IProjectStack? stack = _stacks.FindStack(intent.Stack);
        if (stack is null || stack.Stack != intent.Stack || !stack.SupportedLanguages.Contains(intent.Language, StringComparer.Ordinal))
        {
            throw new InvalidTemplateMetadataException("The configuration action must name an installed canonical stack and supported canonical language.");
        }
    }

    private static void RequireSuccess(ITemplateCreationResult result, string source)
    {
        if (result.Status != CreationResultStatus.Success)
        {
            throw new TemplatePreparationException(
                TemplatePreparationFailure.RenderFailed, $"{source}: native rendering returned {result.Status}: {result.ErrorMessage}");
        }
    }

    private static bool IsWithin(string root, string path)
        => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string NormalizeNativePath(string path)
    {
        string logical = path.Replace('\\', '/');
        while (logical.StartsWith("./", StringComparison.Ordinal))
        {
            logical = logical[2..];
        }

        return PreparedOutputEntry.NormalizeRelativePath(logical);
    }

    internal static InputDataSet CreateInput(ITemplateInfo template, TemplateArgumentParseResult arguments)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(arguments);
        ParameterDefinitionSet definitions = new(template.ParameterDefinitions);
        List<InputParameterData> values = [];
        foreach (ITemplateParameter parameter in template.ParameterDefinitions)
        {
            if (arguments.ValuesByCanonicalName.TryGetValue(parameter.Name, out string? value))
            {
                values.Add(new(parameter, value,
                    arguments.ExplicitParameters.Contains(parameter.Name) ? DataSource.User : DataSource.Default,
                    value.Length == 0 ? InputDataState.ExplicitEmpty : InputDataState.Set));
            }
        }

        return new(definitions, values);
    }

    private sealed class NativeSession(IEnumerable<PackageTemplateCandidate> candidates, Func<ValueTask> release) : PackageTemplateSession(candidates)
    {
        private readonly Func<ValueTask> _release = release ?? throw new ArgumentNullException(nameof(release));
        private readonly Lock _gate = new();
        private Task? _releaseTask;

        public override ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                return new(_releaseTask ??= ReleaseCoreAsync());
            }
        }

        private async Task ReleaseCoreAsync() => await _release();
    }

    private sealed class NativeCandidate(
        NativePackageTemplateAdapter owner,
        TemplateDescriptor descriptor,
        FuncTemplateConstraintOutcome eligibility,
        IEnumerable<string> diagnostics,
        ITemplateInfo template,
        NativeTemplateMetadata raw,
        TemplateEngineSession engine,
        TemplateEngineContext context,
        string workspace,
        string digest) : PackageTemplateCandidate(descriptor, eligibility, diagnostics)
    {
        private readonly NativePackageTemplateAdapter _owner = owner;
        public ITemplateInfo Template { get; } = template;
        public NativeTemplateMetadata Raw { get; } = raw;
        public TemplateEngineSession Engine { get; } = engine;
        public TemplateEngineContext Context { get; } = context;
        public string Workspace { get; } = workspace;
        public string Digest { get; } = digest;

        protected override Task<PackageTemplateCapture> RenderCoreAsync(TemplateArgumentParseResult arguments, string? name, CancellationToken cancellationToken)
            => _owner.RenderAsync(this, arguments, name, cancellationToken);
    }
}
