// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates;

internal interface INewExecutionContextResolver
{
    public Task<NewExecutionContextResult> ResolveAsync(NewExecutionRequest request, bool canPrompt, CancellationToken cancellationToken);
}

internal sealed record NewExecutionContextResult(TemplateEngineContext? Context, NewExecutionResult? Failure);

internal sealed class NewExecutionContextResolver(
    IFileSystem fileSystem,
    IFunctionsProjectResolver projects,
    IProfileResolver profiles,
    IOptionsMonitor<StackOptions> stackOptions,
    IEnumerable<IProjectInitializer> initializers,
    IHostJsonBundleSectionReader hostReader,
    IExtensionBundleResolver bundles,
    TemplateEngineBundleContextReader? bundleContextReader = null,
    bool enableBundleBindingConstraints = false) : INewExecutionContextResolver
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IFunctionsProjectResolver _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly IProfileResolver _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly IOptionsMonitor<StackOptions> _stackOptions = stackOptions ?? throw new ArgumentNullException(nameof(stackOptions));
    private readonly IReadOnlyList<IProjectInitializer> _initializers = [.. initializers ?? throw new ArgumentNullException(nameof(initializers))];
    private readonly IHostJsonBundleSectionReader _hostReader = hostReader ?? throw new ArgumentNullException(nameof(hostReader));
    private readonly IExtensionBundleResolver _bundles = bundles ?? throw new ArgumentNullException(nameof(bundles));
    private readonly TemplateEngineBundleContextReader? _bundleContextReader = enableBundleBindingConstraints
        ? bundleContextReader ?? throw new ArgumentNullException(nameof(bundleContextReader))
        : bundleContextReader;

    public Task<NewExecutionContextResult> ResolveAsync(NewExecutionRequest request, bool canPrompt, CancellationToken cancellationToken)
        => ResolveCoreAsync(request, canPrompt, null, cancellationToken);

    internal Task<NewExecutionContextResult> ResolveNativeAsync(NewExecutionRequest request, bool canPrompt,
        InstalledProjectStackCatalog stackCatalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stackCatalog);
        return ResolveCoreAsync(request, canPrompt, stackCatalog, cancellationToken);
    }

    private async Task<NewExecutionContextResult> ResolveCoreAsync(NewExecutionRequest request, bool canPrompt,
        InstalledProjectStackCatalog? stackCatalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.WorkingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryInfo? directory = request.WorkingDirectory.Info;
        while (directory is not null && !_fileSystem.DirectoryExists(directory.FullName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_fileSystem.FileExists(directory.FullName))
            {
                return Fail("invalidPath", $"'{directory.FullName}' is a file. Use --path with a directory inside a Functions project.");
            }

            directory = directory.Parent;
        }

        FunctionsProject? project = null;
        for (; directory is not null; directory = directory.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_fileSystem.FileExists(Path.Combine(directory.FullName, "host.json"))
                && !_fileSystem.FileExists(CliConfigurationPathsOptions.GetProjectConfigPath(directory)))
            {
                continue;
            }

            ProjectResolutionResult resolution = await _projects.ResolveProjectAsync(
                new(new(directory, request.WorkingDirectory.WasExplicit)), cancellationToken);
            if (resolution is ProjectResolutionResult.Resolved resolved)
            {
                project = resolved.Project;
                break;
            }

            if (resolution is ProjectResolutionResult.NotResolved failed)
            {
                return Fail("projectUnavailable", $"{failed.Message} Run 'func setup' to repair the installed stack, or 'func init' to adopt this project.");
            }
        }

        if (project is null)
        {
            return Fail("projectRequired", "No containing Functions project was found. Run 'func init' to create or adopt a project.");
        }

        WorkingDirectory root = project.WorkingDirectory;
        string stack = project.StackName;
        IReadOnlyList<string> supportedLanguages;
        IReadOnlyDictionary<string, IReadOnlyList<string>> languageAliases;
        if (stackCatalog is not null)
        {
            IProjectStack? installedStack = stackCatalog.FindStack(stack);
            if (installedStack is null)
            {
                return Fail("languageMetadataUnavailable",
                    $"Cannot resolve language metadata for stack '{stack}'. Run 'func setup' to repair the installed stack workload.");
            }

            stack = installedStack.Stack;
            supportedLanguages = installedStack.SupportedLanguages;
            languageAliases = installedStack.SupportedLanguageAliases;
        }
        else
        {
            IProjectInitializer[] matches = [.. _initializers.Where(initializer => string.Equals(initializer.Stack, stack, StringComparison.OrdinalIgnoreCase))];
            if (matches.Length != 1)
            {
                return Fail("languageMetadataUnavailable",
                    $"Cannot resolve language metadata for stack '{stack}'. Run 'func setup' to repair the installed stack workload.");
            }

            supportedLanguages = matches[0].SupportedLanguages;
            languageAliases = matches[0].SupportedLanguageAliases;
        }

        StackOptions options;
        try
        {
            options = _stackOptions.Get(root.Info.FullName);
        }
        catch (InvalidDataException exception)
        {
            return Fail("invalidConfiguration", $"Cannot parse CLI configuration for '{root.Info.FullName}'. Repair config.json and retry. {exception.Message}");
        }
        catch (IOException exception)
        {
            return Fail("invalidConfiguration", $"Cannot load CLI configuration for '{root.Info.FullName}'. Repair config.json and retry. {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Fail("invalidConfiguration", $"Cannot read CLI configuration for '{root.Info.FullName}'. Check config.json permissions and retry. {exception.Message}");
        }

        string? requestedLanguage = request.ExplicitLanguage ?? options.Language ?? project.Language;
        string[] languages = requestedLanguage is null
            ? [.. supportedLanguages.Count == 1 ? supportedLanguages : []]
            : [.. supportedLanguages.Where(language => string.Equals(language, requestedLanguage, StringComparison.OrdinalIgnoreCase)
                || (languageAliases.TryGetValue(language, out IReadOnlyList<string>? aliases)
                    && aliases.Contains(requestedLanguage, StringComparer.OrdinalIgnoreCase)))];
        if (languages.Length != 1)
        {
            return Fail("invalidLanguage", requestedLanguage is null
                ? $"Cannot determine the project language. Supply --language <{string.Join("|", supportedLanguages)}> or run 'func init' to repair the project."
                : $"Language '{requestedLanguage}' is not supported by stack '{stack}'. Use --language with one of: {string.Join(", ", supportedLanguages)}.");
        }

        ProfileResolution profile;
        try
        {
            profile = await _profiles.ResolveAsync(new(root.Info, RequestedProfileName: null, CanPrompt: canPrompt), cancellationToken);
        }
        catch (ProfileConfigurationException exception)
        {
            return Fail("invalidProfile", exception.Message.Replace(
                "Specify --profile or set defaultProfile in .func/config.json.",
                "Set defaultProfile in .func/config.json, or run 'func profile set <name> <project-directory>' to choose a project default.",
                StringComparison.Ordinal));
        }

        HostJsonBundleSection? section;
        try
        {
            section = await _hostReader.ReadAsync(root.Info, cancellationToken);
        }
        catch (ExtensionBundleConfigurationException exception)
        {
            return Fail("invalidBundle", exception.Message);
        }

        TemplateEngineBundleContext? bundle = null;
        if (section is not null)
        {
            if (!BundleHelpers.TryGetBundleChannel(section.Id, out _))
            {
                return Fail("invalidBundle", $"Unrecognized extension bundle '{section.Id}'. Repair extensionBundle in host.json or run 'func init'.");
            }

            ResolvedProfile? resolvedProfile = (profile as ProfileResolution.Resolved)?.Profile;
            ExtensionBundleResolution resolution = await _bundles.ResolveAsync(new(
                section.Id, section.Version, stack, resolvedProfile?.Name, resolvedProfile?.ExtensionBundleVersionRange?.ToString()), cancellationToken);
            if (resolution is not ExtensionBundleResolution.Resolved resolved)
            {
                string diagnostic = resolution switch
                {
                    ExtensionBundleResolution.WorkloadMissing missing => missing.Hint,
                    ExtensionBundleResolution.NoCompatibleInstall missing => missing.Hint,
                    ExtensionBundleResolution.EmptyIntersection empty => empty.Hint,
                    ExtensionBundleResolution.NotResolved missing => missing.Message,
                    _ => throw new InvalidOperationException($"Unknown bundle resolution: {resolution.GetType().Name}"),
                };
                return Fail("bundleUnavailable", diagnostic);
            }

            bundle = enableBundleBindingConstraints
                ? await _bundleContextReader!.ReadAsync(resolved, cancellationToken)
                : new(resolved.BundleId, resolved.Version, resolved.Path);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(new(request.WorkingDirectory, new(root, stack, languages[0]), bundle, enableBundleBindingConstraints, request.OutputIntent), null);
    }

    private static NewExecutionContextResult Fail(string kind, string diagnostic) => new(null, NewExecutionResult.Reject(kind, diagnostic));
}
