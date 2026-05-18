// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Resolves a directory as a Node Functions project when <c>host.json</c> is present
/// alongside a <c>package.json</c>, <c>tsconfig.json</c>, or a <c>*.js</c>/<c>*.mjs</c>/<c>*.cjs</c>/<c>*.ts</c>
/// file at the project root. A <c>package.json</c> declaring <c>@azure/functions</c> is the strongest signal.
/// </summary>
internal sealed class NodeProjectResolver : IProjectResolver
{
    private const string WorkerRuntime = "node";
    private const string FunctionsPackage = "@azure/functions";

    private static readonly string[] _sourceFilePatterns = ["*.js", "*.mjs", "*.cjs", "*.ts"];

    public Task<EvaluationResult> EvaluateAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!workingDirectory.Exists || !File.Exists(Path.Combine(workingDirectory.FullName, "host.json")))
        {
            return Task.FromResult(EvaluationResult.NoMatch("no host.json"));
        }

        string packageJsonPath = Path.Combine(workingDirectory.FullName, "package.json");
        if (File.Exists(packageJsonPath))
        {
            string reason = DeclaresFunctionsPackage(packageJsonPath)
                ? "package.json declares @azure/functions"
                : "found package.json";
            return Task.FromResult(EvaluationResult.Match(reason, WorkerRuntime));
        }

        if (File.Exists(Path.Combine(workingDirectory.FullName, "tsconfig.json")))
        {
            return Task.FromResult(EvaluationResult.Match("found tsconfig.json", WorkerRuntime));
        }

        foreach (string pattern in _sourceFilePatterns)
        {
            if (Directory.EnumerateFiles(workingDirectory.FullName, pattern, SearchOption.TopDirectoryOnly).Any())
            {
                return Task.FromResult(EvaluationResult.Match($"found {pattern} file", WorkerRuntime));
            }
        }

        return Task.FromResult(EvaluationResult.NoMatch("host.json present but no Node fingerprint file"));
    }

    private static bool DeclaresFunctionsPackage(string packageJsonPath)
    {
        // Parse just enough of package.json to spot @azure/functions in
        // dependencies or devDependencies. Malformed JSON falls through
        // to the weaker "found package.json" signal rather than throwing.
        try
        {
            using FileStream stream = File.OpenRead(packageJsonPath);
            using var doc = JsonDocument.Parse(stream);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return HasFunctionsDependency(doc.RootElement, "dependencies")
                || HasFunctionsDependency(doc.RootElement, "devDependencies");
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasFunctionsDependency(JsonElement root, string sectionName)
    {
        return root.TryGetProperty(sectionName, out JsonElement section)
            && section.ValueKind == JsonValueKind.Object
            && section.TryGetProperty(FunctionsPackage, out _);
    }
}
