// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Project initializer for .NET (C# and F#) Azure Functions.
/// Uses the <c>Microsoft.Azure.Functions.Worker.ProjectTemplates</c> NuGet
/// template package to scaffold projects via <c>dotnet new func</c>.
/// </summary>
internal sealed class DotNetProjectInitializer(IDotnetCliRunner dotnetCli) : IProjectInitializer
{
    internal const string TemplatesPackageName = "Microsoft.Azure.Functions.Worker.ProjectTemplates";
    internal const string TemplatesPackageVersion = "4.0.5544";
    internal const string TemplateShortName = "func";
    internal const string DefaultFramework = "net10.0";

    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));

    public string Stack => "dotnet";

    public IReadOnlyList<string> SupportedLanguages => ["C#", "F#", "csharp", "fsharp"];

    public Option<string> FrameworkOption { get; } = new("--target-framework")
    {
        Description = "The target framework for the project (e.g. net8.0, net10.0).",
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
        string? projectName = context.ProjectName ?? Path.GetFileName(projectPath);
        string language = NormalizeLanguage(context.Language);
        string framework = parseResult.GetValue(FrameworkOption) ?? DefaultFramework;

        await EnsureTemplatesInstalledAsync(cancellationToken);

        List<string> args =
        [
            "new", TemplateShortName,
            "--name", projectName,
            "--output", projectPath,
            "--language", language,
            "--Framework", framework,
        ];

        if (context.Force)
        {
            args.Add("--force");
        }

        await _dotnetCli.RunAsync(args, projectPath, cancellationToken);
    }

    internal async Task EnsureTemplatesInstalledAsync(CancellationToken cancellationToken)
    {
        await _dotnetCli.RunAsync(
            ["new", "install", $"{TemplatesPackageName}@{TemplatesPackageVersion}"],
            workingDirectory: null,
            cancellationToken);
    }

    internal static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "C#";
        }

        return language.ToUpperInvariant() switch
        {
            "CSHARP" or "C#" => "C#",
            "FSHARP" or "F#" => "F#",
            _ => language
        };
    }
}
