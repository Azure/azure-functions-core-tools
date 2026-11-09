// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Collections.Immutable;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Projects.FileWriting;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Retains the function scaffolding command surface during the Template Engine migration.
/// </summary>
internal sealed class NewCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "Function name.",
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template ID.",
    };

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Overwrite existing files.",
    };

    public Option<bool> NonInteractiveOption { get; } = new("--non-interactive")
    {
        Description = "Refuse to prompt.",
    };

    public Option<bool> ListOption { get; } = new("--list", "-l")
    {
        Description = "List available templates (temporarily unavailable).",
    };

    public Option<string?> FormatOption { get; } = new("--format")
    {
        Description = "Output mode (plain | json). Defaults to plain.",
    };

    private readonly IInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly InstalledProjectStackCatalog _projectStacks;
    private readonly ITemplaterFactory _templaterFactory;
    private readonly ProjectTemplateInputLease _inputLease;
    private readonly ProjectFileWriter _fileWriter;

    public NewCommand(
        IInteractionService interaction,
        IFunctionsProjectResolver projectResolver,
        InstalledProjectStackCatalog projectStacks,
        ITemplaterFactory templaterFactory,
        ProjectTemplateInputLease inputLease,
        ProjectFileWriter fileWriter)
        : base("new", "Create a new function from a template.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
        _projectStacks = projectStacks ?? throw new ArgumentNullException(nameof(projectStacks));
        _templaterFactory = templaterFactory ?? throw new ArgumentNullException(nameof(templaterFactory));
        _inputLease = inputLease ?? throw new ArgumentNullException(nameof(inputLease));
        _fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(ForceOption);
        Options.Add(NonInteractiveOption);
        Options.Add(ListOption);
        Options.Add(FormatOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (parseResult.GetValue(ListOption))
        {
            _interaction.WriteError("Template listing is not available in this migration increment.");
            return 1;
        }

        if (parseResult.GetValue(ForceOption))
        {
            _interaction.WriteError("Overwriting files is not supported for template generation; existing files are preserved.");
            return 1;
        }

        if (string.Equals(parseResult.GetValue(FormatOption), "json", StringComparison.OrdinalIgnoreCase))
        {
            _interaction.WriteError("JSON output is not available for template generation in this increment.");
            return 1;
        }

        string? reference = parseResult.GetValue(TemplateOption);
        if (string.IsNullOrWhiteSpace(reference))
        {
            _interaction.WriteError("Select an item template with --template.");
            return 1;
        }

        string name = parseResult.GetValue(NameOption) ?? "Function1";
        WorkingDirectory directory = parseResult.GetValue(PathArgument ?? throw new InvalidOperationException("The command path argument is missing."))
            ?? throw new InvalidOperationException("The command path argument has no value.");
        ProjectResolutionResult projectResolution = await _projectResolver.ResolveProjectAsync(new(directory), cancellationToken);
        if (projectResolution is not ProjectResolutionResult.Resolved resolved)
        {
            string diagnostic = projectResolution switch
            {
                ProjectResolutionResult.Uninitialized uninitialized => uninitialized.Message,
                ProjectResolutionResult.NotResolved notResolved => notResolved.Message,
                _ => throw new InvalidOperationException($"Unexpected project resolution result: {projectResolution.GetType().Name}.")
            };
            _interaction.WriteError(diagnostic);
            return 1;
        }

        FunctionsProject project = resolved.Project;
        IProjectStack? stack = _projectStacks.FindStack(project.StackName);
        if (stack is null)
        {
            _interaction.WriteError($"The '{project.StackName}' project stack is not installed.");
            return 1;
        }

        ProjectTemplateCompatibility compatibility;
        try
        {
            compatibility = await stack.EvaluateTemplateCompatibilityAsync(project, _inputLease.Pin, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or ArgumentException)
        {
            _interaction.WriteError($"The project could not be safely evaluated for template compatibility: {exception.Message}");
            return 1;
        }

        using (compatibility)
        {
            if (!compatibility.IsCompatible)
            {
                _interaction.WriteError(compatibility.Diagnostic!);
                return 1;
            }

            var engineBindings = compatibility.Bindings
                .Where(binding => binding.Key.Contains(':'))
                .ToDictionary(binding => binding.Key, binding => binding.Value, StringComparer.Ordinal);
            var templateParameters = compatibility.Bindings
                .Where(binding => !binding.Key.Contains(':'))
                .ToDictionary(binding => binding.Key, binding => binding.Value, StringComparer.Ordinal);
            if (engineBindings.Count + templateParameters.Count != compatibility.Bindings.Count
                || templateParameters.Keys.Any(parameter => string.Equals(parameter, "name", StringComparison.OrdinalIgnoreCase))
                || engineBindings.Any(binding => string.IsNullOrWhiteSpace(binding.Value)))
            {
                _interaction.WriteError("The workload returned unsupported template input values.");
                return 1;
            }

            TemplateEngineContext context = new(
                directory,
                new TemplateEngineProjectContext(project.WorkingDirectory, project.StackName, project.Language),
                bindings: engineBindings);

            Templater templater;
            try
            {
                templater = await _templaterFactory.CreateAsync(context, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
            {
                _interaction.WriteError($"Installed templates are unavailable: {exception.Message}");
                return 1;
            }

            using (templater)
            {
                TemplateResolutionResult resolution;
                try
                {
                    resolution = await templater.ResolveAsync(reference, TemplateType.Item, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or ArgumentException)
                {
                    _interaction.WriteError($"Installed templates could not be resolved: {exception.Message}");
                    return 1;
                }

                if (resolution.Status != TemplateResolutionStatus.Selected || resolution.SelectedTemplate is null)
                {
                    string diagnostic = string.Join("; ", resolution.Diagnostics.Select(item => item.Message));
                    _interaction.WriteError(string.IsNullOrWhiteSpace(diagnostic)
                        ? $"Template '{reference}' could not be selected."
                        : diagnostic);
                    return 1;
                }

                if (templateParameters.Keys.Any(parameter => !resolution.SelectedTemplate.Descriptor.Parameters.Any(
                    definition => string.Equals(definition.CanonicalName, parameter, StringComparison.Ordinal))))
                {
                    _interaction.WriteError("The workload returned template values that the selected template does not accept.");
                    return 1;
                }

                TemplateRenderResult rendered;
                try
                {
                    rendered = await templater.RenderAsync(resolution.SelectedTemplate, name, templateParameters, cancellationToken);
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
                {
                    _interaction.WriteError($"Template generation failed before project changes: {exception.Message}");
                    return 1;
                }

                if (stack is not IProjectTemplateTasks tasks)
                {
                    _interaction.WriteError($"The '{stack.Stack}' workload cannot validate this template's effects.");
                    return 1;
                }

                IReadOnlyList<(Guid ActionId, IReadOnlyDictionary<string, string> Arguments, bool ContinueOnError)> pendingTasks;
                try
                {
                    pendingTasks = await templater.PrepareTasksAsync(rendered, tasks, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
                {
                    _interaction.WriteError($"The template requires an unsupported or unverified project effect: {exception.Message}");
                    return 1;
                }

                if (pendingTasks.Count > 0)
                {
                    _interaction.WriteError("The selected item template requires project changes that are not supported in this increment.");
                    return 1;
                }

                ProjectWriteResult writeResult;
                try
                {
                    writeResult = await _fileWriter.WriteAsync(directory.Info.FullName, rendered.Files, rendered.Directories, cancellationToken);
                }
                catch (Exception exception) when (exception.Data[typeof(ProjectWriteResult)] is ProjectWriteResult partialResult)
                {
                    ReportWriteEffects(partialResult);
                    throw new GracefulException($"Writing template files failed after partial project effects: {exception.Message}", exception, isUserError: true);
                }

                if (writeResult.Canceled)
                {
                    ReportWriteEffects(writeResult);
                    throw writeResult.Cancellation!;
                }

                ReportWriteEffects(writeResult);
                if (!writeResult.Succeeded)
                {
                    return 1;
                }

                IReadOnlyList<string> taskFailures;
                try
                {
                    taskFailures = await templater.ExecuteTasksAsync(pendingTasks, tasks, cancellationToken);
                }
                catch (GracefulException exception)
                {
                    _interaction.WriteError($"Template task failed after generated files were written: {exception.Message}");
                    return 1;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _interaction.WriteError("Template task was canceled after generated files were written; no rollback was attempted.");
                    throw;
                }

                foreach (string failure in taskFailures)
                {
                    _interaction.WriteError($"A template task failed but the template allowed processing to continue: {failure}");
                }

                if (taskFailures.Count > 0)
                {
                    return 1;
                }

                _interaction.WriteSuccess($"Created '{name}' from template '{rendered.TemplateIdentity}'.");
                return 0;
            }
        }
    }

    private void ReportWriteEffects(ProjectWriteResult result)
    {
        foreach (ProjectWriteEffect effect in result.Effects)
        {
            string path = effect.IsDirectory && effect.Path.Length == 0 ? "project directory" : effect.Path;
            switch (effect.Status)
            {
                case ProjectWriteStatus.Created:
                    _interaction.WriteLine($"Created {path}.");
                    break;
                case ProjectWriteStatus.Preserved:
                    _interaction.WriteLine($"Preserved existing {path}.");
                    break;
                case ProjectWriteStatus.Failed:
                    _interaction.WriteError($"Could not create {path}: {effect.Error?.Message ?? "unknown filesystem error"}");
                    break;
                case ProjectWriteStatus.RemovedAfterFailure:
                    _interaction.WriteWarning($"Removed {path} after a failed write.");
                    break;
                case ProjectWriteStatus.CleanupPending:
                case ProjectWriteStatus.Uncertain:
                    _interaction.WriteWarning($"The effect on {path} is uncertain: {effect.CleanupError?.Message ?? effect.CompletionError?.Message ?? effect.Error?.Message}");
                    break;
                case ProjectWriteStatus.NotAttempted:
                    _interaction.WriteLine($"Not attempted: {path}.");
                    break;
            }
        }
    }
}
