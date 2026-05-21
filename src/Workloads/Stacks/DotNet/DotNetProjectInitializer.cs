// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.ComponentModel;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Project initializer for .NET (C# and F#) Azure Functions.
/// Uses the <c>Microsoft.Azure.Functions.Worker.ProjectTemplates</c> NuGet
/// template package to scaffold projects via <c>dotnet new func</c>.
/// </summary>
internal sealed class DotNetProjectInitializer(IDotnetCliRunner dotnetCli, ITemplateHivePathProvider hivePathProvider) : IProjectInitializer
{
    internal const string TemplatesPackageName = "Microsoft.Azure.Functions.Worker.ProjectTemplates";
    internal const string TemplateShortName = "func";
    internal const string DefaultFramework = "net10.0";

    internal static readonly IReadOnlyList<string> SupportedFrameworks = ["net10.0"];

    internal static readonly TimeSpan HiveTtl = TimeSpan.FromDays(30);
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(5);

    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));
    private readonly ITemplateHivePathProvider _hivePathProvider = hivePathProvider ?? throw new ArgumentNullException(nameof(hivePathProvider));

    public string Stack => "dotnet";

    public IReadOnlyList<string> SupportedLanguages => ["C#", "F#", "csharp", "fsharp"];

    public Option<string> FrameworkOption { get; } = new("--target-framework", "-tfm")
    {
        Description = "The target framework for the project (e.g. net10.0).",
        DefaultValueFactory = _ => DefaultFramework
    };

    public IReadOnlyList<Option> GetInitOptions() => [FrameworkOption];

    public async Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);

        string projectPath = context.WorkingDirectory.Info.FullName;

        string language = NormalizeLanguage(context.Language);
        if (!SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Language '{context.Language}' is not supported. Supported languages: {string.Join(", ", SupportedLanguages)}.", nameof(context));
        }

        string projectName = context.ProjectName ?? Path.GetFileName(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName, nameof(context.ProjectName));

        string framework = parseResult.GetValue(FrameworkOption) ?? DefaultFramework;
        if (!SupportedFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Framework '{framework}' is not supported. Supported frameworks: {string.Join(", ", SupportedFrameworks)}.", nameof(framework));
        }

        try
        {
            await EnsureTemplatesInstalledAsync(cancellationToken);
        }
        catch (DotnetCliException ex)
        {
            throw new GracefulException(
                $"Failed to install project templates: {ex.Message}",
                isUserError: true);
        }

        Directory.CreateDirectory(projectPath);

        List<string> args =
        [
            "new", TemplateShortName,
            "--name", projectName,
            "--output", projectPath,
            "--language", language,
            "--framework", framework,
            "--debug:custom-hive", _hivePathProvider.HivePath,
        ];

        if (context.Force)
        {
            args.Add("--force");
        }

        try
        {
            await RunWithTimeoutAsync(args, projectPath, cancellationToken);
        }
        catch (DotnetCliException ex)
        {
            throw new GracefulException(
                $"Failed to scaffold project: {ex.Message}",
                isUserError: true);
        }
    }

    internal async Task EnsureTemplatesInstalledAsync(CancellationToken cancellationToken)
    {
        if (IsHiveFresh())
        {
            return;
        }

        await RunWithTimeoutAsync(
            ["new", "install", TemplatesPackageName, "--debug:custom-hive", _hivePathProvider.HivePath],
            workingDirectory: null,
            cancellationToken);

        WriteTimestamp();
    }

    private async Task RunWithTimeoutAsync(
        IReadOnlyList<string> args,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = new(OperationTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            await _dotnetCli.RunAsync(args, workingDirectory, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new GracefulException(
                $"The dotnet CLI operation timed out after {OperationTimeout.TotalMinutes:0} minutes. Check your network connection and try again.",
                isUserError: true);
        }
    }

    private bool IsHiveFresh()
    {
        try
        {
            string hivePath = _hivePathProvider.HivePath;
            string timestampPath = _hivePathProvider.TimestampPath;

            if (!File.Exists(timestampPath))
            {
                return false;
            }

            if (!Directory.Exists(hivePath))
            {
                return false;
            }

            bool hasTemplates = Directory
                .EnumerateFileSystemEntries(hivePath)
                .Any(e => !string.Equals(Path.GetFileName(e), ".installed", StringComparison.Ordinal));

            if (!hasTemplates)
            {
                return false;
            }

            DateTime installedUtc = File.GetLastWriteTimeUtc(timestampPath);
            return DateTime.UtcNow - installedUtc < HiveTtl;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void WriteTimestamp()
    {
        try
        {
            Directory.CreateDirectory(_hivePathProvider.HivePath);
            File.WriteAllText(_hivePathProvider.TimestampPath, string.Empty);
        }
        catch (IOException)
        {
            // Non-fatal; next run will simply reinstall.
        }
    }

    internal static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "c#";
        }

        return language.ToLowerInvariant() switch
        {
            "csharp" or "c#" => "c#",
            "fsharp" or "f#" => "f#",
            _ => language
        };
    }
}
