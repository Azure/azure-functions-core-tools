// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Initializes a new Azure Functions project. Only universal options are defined
/// here — workloads contribute their own language-specific options (e.g., dotnet
/// adds --target-framework, python adds --model) via IProjectInitializer.GetInitOptions().
/// </summary>
public class InitCommand : BaseCommand
{
    public static readonly Option<string?> WorkerRuntimeOption = new("--worker-runtime", "-w")
    {
        Description = "The worker runtime for the project"
    };

    public static readonly Option<string?> NameOption = new("--name", "-n")
    {
        Description = "The name of the function app project"
    };

    public static readonly Option<string?> LanguageOption = new("--language", "-l")
    {
        Description = "The programming language (e.g., C#, F#, JavaScript, TypeScript, Python)"
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Force initialization even if the folder is not empty"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadManager? _workloadManager;

    public InitCommand(IInteractionService interaction, IWorkloadManager? workloadManager = null)
        : base("init", "Initialize a new Azure Functions project.")
    {
        _interaction = interaction;
        _workloadManager = workloadManager;

        AddPathArgument();
        Options.Add(WorkerRuntimeOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        // Let installed workloads contribute their options to this command
        RegisterWorkloadOptions();

        // Update the --worker-runtime description to list installed runtimes
        UpdateRuntimeDescription();
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        var workerRuntime = parseResult.GetValue(WorkerRuntimeOption);
        var force = parseResult.GetValue(ForceOption);
        var name = parseResult.GetValue(NameOption);
        var language = parseResult.GetValue(LanguageOption);

        return await RunInitAsync(workerRuntime, language, name, force, parseResult, cancellationToken);
    }

    /// <summary>
    /// Runs the init flow. Can be called from other commands (e.g., func new)
    /// to initialize a project before proceeding. Pass null for parseResult
    /// when calling outside of a parsed init command.
    /// </summary>
    internal async Task<int> RunInitAsync(
        string? workerRuntime,
        string? language,
        string? name,
        bool force,
        ParseResult? parseResult,
        CancellationToken cancellationToken)
    {
        // Check for an existing project
        var existingRuntime = DetectExistingProject();
        if (existingRuntime is not null && !force)
        {
            _interaction.WriteError(
                $"Directory already contains a '{existingRuntime}' project. Use --force to overwrite.");
            return 1;
        }

        if (existingRuntime is not null && force)
        {
            CleanProjectFiles();
        }

        // If no worker runtime specified, prompt for one
        if (string.IsNullOrEmpty(workerRuntime))
        {
            workerRuntime = await PromptForRuntimeAsync(cancellationToken);
            if (string.IsNullOrEmpty(workerRuntime))
            {
                return 1;
            }
        }

        // Find a project initializer for the requested runtime
        var initializer = _workloadManager?
            .GetAllProjectInitializers()
            .FirstOrDefault(p => p.CanHandle(workerRuntime));

        if (initializer is null)
        {
            // Offer to install the workload
            return await OfferWorkloadInstall(workerRuntime, parseResult, cancellationToken);
        }

        // If no language specified via CLI, prompt if multiple are available
        if (string.IsNullOrEmpty(language))
        {
            if (initializer.SupportedLanguages.Count > 1)
            {
                language = await _interaction.PromptForSelectionAsync(
                    "Select a language:",
                    initializer.SupportedLanguages,
                    cancellationToken);
            }
            else if (initializer.SupportedLanguages.Count == 1)
            {
                language = initializer.SupportedLanguages[0];
            }
        }

        var context = new ProjectInitContext(
            ProjectPath: Directory.GetCurrentDirectory(),
            WorkerRuntime: workerRuntime,
            Language: language,
            ProjectName: name,
            Force: force);

        await _interaction.StatusAsync(
            "Initializing project...",
            async ct => await initializer.InitializeAsync(context, parseResult ?? Parse(""), ct),
            cancellationToken);

        _interaction.WriteSuccess($"Azure Functions project initialized with '{workerRuntime}' runtime.");
        return 0;
    }

    /// <summary>
    /// Queries all installed workloads and adds their init options to this command.
    /// This means 'func init -h' shows options from all installed workloads.
    /// Option descriptions are prefixed with the workload name for clarity.
    /// </summary>
    private void RegisterWorkloadOptions()
    {
        if (_workloadManager is null) return;

        var registeredOptions = new HashSet<string>();
        foreach (var initializer in _workloadManager.GetAllProjectInitializers())
        {
            var label = $"[{initializer.WorkerRuntime}] ";
            foreach (var option in initializer.GetInitOptions())
            {
                // Avoid duplicates if multiple workloads define the same option
                if (registeredOptions.Add(option.Name))
                {
                    // Prefix description so help output shows which workload owns the option
                    if (option.Description is not null && !option.Description.StartsWith('['))
                    {
                        option.Description = label + option.Description;
                    }

                    Options.Add(option);
                }
            }
        }
    }

    /// <summary>
    /// Updates the --worker-runtime description to list installed runtimes.
    /// </summary>
    private void UpdateRuntimeDescription()
    {
        var runtimes = _workloadManager?.GetAvailableRuntimes() ?? [];
        if (runtimes.Count > 0)
        {
            WorkerRuntimeOption.Description =
                $"The worker runtime for the project ({string.Join(", ", runtimes)})";
        }
    }

    private async Task<string?> PromptForRuntimeAsync(CancellationToken cancellationToken)
    {
        var runtimes = _workloadManager?.GetAvailableRuntimes() ?? [];

        if (runtimes.Count == 0)
        {
            _interaction.WriteError("No language workloads installed.");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine(
                "[grey]Azure Functions supports many languages through workloads. Install one to get started:[/]");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("  [white]func workload install dotnet[/]       [grey]C#, F#[/]");
            _interaction.WriteMarkupLine("  [white]func workload install node[/]         [grey]JavaScript, TypeScript[/]");
            _interaction.WriteMarkupLine("  [white]func workload install python[/]       [grey]Python[/]");
            _interaction.WriteMarkupLine("  [white]func workload install java[/]         [grey]Java[/]");
            _interaction.WriteMarkupLine("  [white]func workload install powershell[/]   [grey]PowerShell[/]");
            _interaction.WriteBlankLine();
            _interaction.WriteMarkupLine("[grey]Run[/] [white]func workload list[/] [grey]to see installed workloads.[/]");
            return null;
        }

        _interaction.WriteMarkupLine(
            "[white]💡 Other languages (node, python, java, powershell) available via[/] [blue]func workload install <runtime>[/]");
        _interaction.WriteBlankLine();

        var result = await _interaction.PromptForSelectionAsync(
            "Select a worker runtime:",
            runtimes,
            cancellationToken);

        return result;
    }

    private async Task<int> OfferWorkloadInstall(string workerRuntime, ParseResult? parseResult, CancellationToken cancellationToken)
    {
        var packageId = WorkloadManager.ResolvePackageId(workerRuntime);

        _interaction.WriteWarning(
            $"No workload installed for worker runtime '{workerRuntime}'.");
        _interaction.WriteBlankLine();

        if (_interaction.IsInteractive && _workloadManager is not null)
        {
            var install = await _interaction.ConfirmAsync(
                $"Would you like to install the '{workerRuntime}' workload now?",
                defaultValue: true,
                cancellationToken);

            if (install)
            {
                await _interaction.StatusAsync(
                    $"Installing '{workerRuntime}' workload...",
                    async ct => await _workloadManager.InstallWorkloadAsync(
                        packageId, cancellationToken: ct),
                    cancellationToken);

                // Retry — find the initializer from the freshly-installed workload
                var initializer = _workloadManager
                    .GetAllProjectInitializers()
                    .FirstOrDefault(p => p.CanHandle(workerRuntime));

                if (initializer is null)
                {
                    _interaction.WriteError(
                        $"Workload installed but no project initializer found for '{workerRuntime}'.");
                    return 1;
                }

                // Continue with init
                var force = parseResult?.GetValue(ForceOption) ?? false;
                var name = parseResult?.GetValue(NameOption);
                var language = parseResult?.GetValue(LanguageOption);

                if (string.IsNullOrEmpty(language))
                {
                    if (initializer.SupportedLanguages.Count > 1)
                    {
                        language = await _interaction.PromptForSelectionAsync(
                            "Select a language:",
                            initializer.SupportedLanguages,
                            cancellationToken);
                    }
                    else if (initializer.SupportedLanguages.Count == 1)
                    {
                        language = initializer.SupportedLanguages[0];
                    }
                }

                var context = new ProjectInitContext(
                    ProjectPath: Directory.GetCurrentDirectory(),
                    WorkerRuntime: workerRuntime,
                    Language: language,
                    ProjectName: name,
                    Force: force);

                await _interaction.StatusAsync(
                    "Initializing project...",
                    async ct => await initializer.InitializeAsync(context, parseResult ?? Parse(""), ct),
                    cancellationToken);

                _interaction.WriteSuccess($"Azure Functions project initialized with '{workerRuntime}' runtime.");
                return 0;
            }
        }

        _interaction.WriteMarkupLine(
            $"[grey]Install the workload manually:[/] [white]func workload install {workerRuntime}[/]");
        return 1;
    }

    /// <summary>
    /// Detects if the current directory already contains a Functions project.
    /// Returns the detected runtime name, or null if no project is found.
    /// </summary>
    private static string? DetectExistingProject()
    {
        var cwd = Directory.GetCurrentDirectory();

        if (Directory.EnumerateFiles(cwd, "*.csproj").Any()
            || Directory.EnumerateFiles(cwd, "*.fsproj").Any())
        {
            return "dotnet";
        }

        if (File.Exists(Path.Combine(cwd, "package.json")))
        {
            return "node";
        }

        if (File.Exists(Path.Combine(cwd, "requirements.txt"))
            || File.Exists(Path.Combine(cwd, "pyproject.toml")))
        {
            return "python";
        }

        if (File.Exists(Path.Combine(cwd, "pom.xml"))
            || File.Exists(Path.Combine(cwd, "build.gradle")))
        {
            return "java";
        }

        if (File.Exists(Path.Combine(cwd, "profile.ps1")))
        {
            return "powershell";
        }

        return null;
    }

    /// <summary>
    /// Removes well-known project files from the current directory so a fresh
    /// init can proceed. Only deletes files that are typically generated by
    /// <c>func init</c> — user content in subdirectories is preserved.
    /// </summary>
    private void CleanProjectFiles()
    {
        var cwd = Directory.GetCurrentDirectory();

        // Common files generated by func init across all runtimes
        string[] wellKnownFiles =
        [
            "host.json",
            "local.settings.json",
            ".gitignore",
            "Program.cs",
            "Program.fs",
            "package.json",
            "tsconfig.json",
            "requirements.txt",
            "pyproject.toml",
            "function_app.py",
            "profile.ps1",
            "pom.xml",
            "build.gradle",
        ];

        foreach (var file in wellKnownFiles)
        {
            var path = Path.Combine(cwd, file);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // Remove project files by extension (*.csproj, *.fsproj)
        foreach (var pattern in new[] { "*.csproj", "*.fsproj" })
        {
            foreach (var file in Directory.EnumerateFiles(cwd, pattern))
            {
                File.Delete(file);
            }
        }

        // Remove Properties/launchSettings.json if it exists
        var propsDir = Path.Combine(cwd, "Properties");
        if (Directory.Exists(propsDir))
        {
            var launchSettings = Path.Combine(propsDir, "launchSettings.json");
            if (File.Exists(launchSettings))
            {
                File.Delete(launchSettings);
            }

            // Remove Properties dir if now empty
            if (!Directory.EnumerateFileSystemEntries(propsDir).Any())
            {
                Directory.Delete(propsDir);
            }
        }

        _interaction.WriteMarkupLine("[grey]Cleaned existing project files.[/]");
    }
}
