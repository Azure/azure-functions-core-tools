// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/sdk src/Cli/Microsoft.TemplateEngine.Cli/PostActionProcessors/ProcessStartPostActionProcessor.cs.

using System.ComponentModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Processes the 'Run script' post action by running the configured executable in the template output directory.
/// </summary>
/// <remarks>
/// This processor runs whatever the template declares. <see cref="PostActionDispatcher"/> must authorize every
/// <see cref="ActionProcessorId"/> action before it reaches this processor.
/// </remarks>
internal sealed class ProcessStartPostActionProcessor(
    IProcessRunner processRunner, IFileSystem fileSystem, IInteractionService interaction) : ICorePostActionProcessor
{
    internal static readonly Guid ActionProcessorId = new("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2");

    private readonly IProcessRunner _processRunner = processRunner
        ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IInteractionService _interaction = interaction
        ?? throw new ArgumentNullException(nameof(interaction));

    /// <inheritdoc />
    public Guid Id => ActionProcessorId;

    /// <inheritdoc />
    public async Task<bool> ProcessAsync(
        EnginePostActionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, string> args = context.Action.Args ?? new Dictionary<string, string>();
        if (!args.TryGetValue("executable", out string? executable) || string.IsNullOrWhiteSpace(executable))
        {
            _interaction.WriteError(
                "Failed to run the command: argument 'executable' is missing in post action configuration.");
            return false;
        }

        args.TryGetValue("args", out string? arguments);

        // Streams are redirected unless the template explicitly opts out with "false".
        bool redirectStandardOutput = !IsFalse(args, "redirectStandardOutput");
        bool redirectStandardError = !IsFalse(args, "redirectStandardError");

        string outputBasePath = Path.GetFullPath(context.OutputBasePath);
        string command = string.IsNullOrWhiteSpace(arguments) ? executable : $"{executable} {arguments}";
        _interaction.WriteLine($"Running command '{command}'...");

        ProcessRunRequest request = new(
            ResolveExecutableFilePath(executable, outputBasePath), [], outputBasePath, Timeout.InfiniteTimeSpan)
        {
            ArgumentString = arguments,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = redirectStandardError,
        };

        ProcessOutcome outcome;
        try
        {
            outcome = await _processRunner.RunAsync(request, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            _interaction.WriteError("Command failed.");
            _interaction.WriteRawLine(exception.Message);
            return false;
        }

        if (outcome.ExecutableNotFound)
        {
            _interaction.WriteError("Command failed.");
            _interaction.WriteRawLine(
                $"The executable '{executable}' was not found in '{outputBasePath}' or on PATH.");
            return false;
        }

        if (outcome.ExitCode != 0)
        {
            _interaction.WriteError(
                outcome.ExitCode is int exitCode ? $"Command failed with exit code {exitCode}." : "Command failed.");
            WriteCommandOutput(request, outcome);
            return false;
        }

        _interaction.WriteSuccess("Command succeeded.");
        return true;
    }

    private static bool IsFalse(IReadOnlyDictionary<string, string> args, string name)
        => args.TryGetValue(name, out string? value) && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Prefers an executable shipped in the output directory; otherwise the bare name is left to the PATH search.
    /// </summary>
    private string ResolveExecutableFilePath(string executable, string outputBasePath)
    {
        if (_fileSystem.DirectoryExists(outputBasePath))
        {
            // Resolution is intentionally not restricted to the output directory; user consent, not path containment,
            // guards execution. This also aligns with dotnet CLI behavior.
            string combined = Path.Combine(outputBasePath, executable);
            if (_fileSystem.FileExists(combined))
            {
                return combined;
            }
        }

        return executable;
    }

    private void WriteCommandOutput(ProcessRunRequest request, ProcessOutcome outcome)
    {
        if (request.RedirectStandardOutput || request.RedirectStandardError)
        {
            _interaction.WriteLine("Output from command:");
        }

        if (request.RedirectStandardOutput)
        {
            _interaction.WriteLine("StdOut:");
            _interaction.WriteRawLine(string.IsNullOrWhiteSpace(outcome.StandardOutput) ? "(empty)" : outcome.StandardOutput);
        }

        if (request.RedirectStandardError)
        {
            _interaction.WriteLine("StdErr:");
            _interaction.WriteRawLine(string.IsNullOrWhiteSpace(outcome.StandardError) ? "(empty)" : outcome.StandardError);
        }
    }
}
