// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

/// <summary>
/// Evaluates the selected project before native template binding, without restoring or building it.
/// </summary>
internal sealed class DotNetTemplateProjectPreparation(IDotnetCliRunner dotnetCli)
{
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));

    /// <summary>
    /// Reads the actual target framework and namespace through MSBuild, including imported project properties.
    /// </summary>
    /// <exception cref="ArgumentException">The target is not an absolute C# or F# project path.</exception>
    /// <exception cref="DotnetCliException">Project evaluation failed.</exception>
    /// <exception cref="InvalidOperationException">Evaluation is malformed or the project targets an unsupported framework.</exception>
    public async Task<DotNetTemplateProjectContext> ReadAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        string target = DotNetTemplateDependencyExecutor.ValidateProjectPath(projectFilePath);
        cancellationToken.ThrowIfCancellationRequested();
        StringBuilder output = new();
        await _dotnetCli.RunStreamingAsync(["msbuild", target, "-nologo", "-getProperty:TargetFramework,RootNamespace"],
            Path.GetDirectoryName(target), line => output.AppendLine(line), null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = JsonDocument.Parse(output.ToString());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Properties", out JsonElement properties)
                || properties.ValueKind != JsonValueKind.Object
                || !properties.TryGetProperty("TargetFramework", out JsonElement frameworkValue)
                || frameworkValue.ValueKind != JsonValueKind.String
                || !properties.TryGetProperty("RootNamespace", out JsonElement namespaceValue)
                || namespaceValue.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("MSBuild did not return the selected project's TargetFramework and RootNamespace.");
            }

            string framework = frameworkValue.GetString()!;
            string rootNamespace = namespaceValue.GetString()!;
            if (!DotNetTemplateFrameworks.Supported.Contains(framework, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The selected project targets '{framework}'. Supported frameworks: {string.Join(", ", DotNetTemplateFrameworks.Supported)}.");
            }

            if (string.IsNullOrWhiteSpace(rootNamespace))
            {
                throw new InvalidOperationException("The selected project does not declare a usable RootNamespace.");
            }

            return new(target, framework, rootNamespace);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("MSBuild returned invalid project-property JSON.", exception);
        }
    }
}

/// <summary>
/// Carries evaluated properties for the exact project chosen by the command.
/// </summary>
internal sealed record DotNetTemplateProjectContext(string ProjectFilePath, string TargetFramework, string RootNamespace);
