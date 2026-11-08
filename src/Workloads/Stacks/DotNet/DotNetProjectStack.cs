// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

internal sealed class DotNetProjectStack(IDotnetCliRunner dotnetCli) : IProjectStack, IProjectTemplateTasks
{
    private static readonly Guid _restore = new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");
    private static readonly Guid _addReference = new("B17581D1-C5C9-4489-8F0A-004BE667B814");
    private const int MaximumProjectInputs = 256;
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));

    public string Stack => "dotnet";

    public string DisplayName => ".NET";

    public IReadOnlyList<string> WorkerRuntimeAliases { get; } = ["dotnet-isolated"];

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        { "C#", ["csharp"] },
        { "F#", ["fsharp"] }
    };

    public async Task<ProjectTemplateCompatibility> EvaluateTemplateCompatibilityAsync(
        FunctionsProject project,
        Func<IReadOnlyList<string>, CancellationToken, IDisposable> pinInputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(pinInputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (project is not DotNetSourceProject source || !string.Equals(source.Language, "C#", StringComparison.Ordinal))
        {
            return Incompatible("This template requires an initialized C# isolated-worker source project.");
        }

        if (!IsTemplateRouteQualified(source.WorkingDirectory.Info.FullName))
        {
            return Incompatible("Template generation for this project is qualified only on Windows x64 local fixed NTFS or ReFS paths.");
        }

        (string[] Inputs, Dictionary<string, string> Properties, (string Identity, string Version)[] Packages) initial =
            await EvaluateProjectAsync(source, cancellationToken);
        string projectPath = Path.GetFullPath(source.ProjectFilePath);
        if (initial.Inputs.Length == 0)
        {
            throw new InvalidDataException("MSBuild returned no evaluated project inputs.");
        }

        if (initial.Inputs.Length > MaximumProjectInputs)
        {
            throw new InvalidDataException(
                $"MSBuild returned {initial.Inputs.Length} evaluated project inputs, exceeding the {MaximumProjectInputs}-input safety limit.");
        }

        if (!initial.Inputs.Contains(projectPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The evaluated project inputs do not include the source project file.");
        }

        IDisposable? evidence = pinInputs(initial.Inputs, cancellationToken)
            ?? throw new InvalidOperationException("The project input pin operation returned no evidence lease.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string[] Inputs, Dictionary<string, string> Properties, (string Identity, string Version)[] Packages) confirmed =
                await EvaluateProjectAsync(source, cancellationToken);
            if (!SameEvaluation(initial, confirmed))
            {
                throw new InvalidDataException("The project or its imported inputs changed while compatibility was being evaluated.");
            }

            string? diagnostic = GetCompatibilityDiagnostic(confirmed.Properties, confirmed.Packages);
            Dictionary<string, string> bindings = new(StringComparer.Ordinal);
            if (confirmed.Properties.TryGetValue("TargetFramework", out string? targetFramework)
                && !string.IsNullOrWhiteSpace(targetFramework))
            {
                bindings["msbuild:TargetFramework"] = targetFramework;
            }

            if (confirmed.Properties.TryGetValue("RootNamespace", out string? rootNamespace)
                && !string.IsNullOrWhiteSpace(rootNamespace))
            {
                bindings["namespace"] = rootNamespace;
            }

            ProjectTemplateCompatibility result = new(diagnostic is null, diagnostic, bindings, evidence);
            evidence = null;
            return result;
        }
        finally
        {
            evidence?.Dispose();
        }
    }

    public async Task<bool> RequiresExecutionAsync(
        WorkingDirectory directory, Guid actionId, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        if (actionId == _restore && arguments.Count == 0)
        {
            return true;
        }

        if (actionId != _addReference
            || arguments.Count != 4
            || !arguments.TryGetValue("referenceType", out string? referenceType)
            || !string.Equals(referenceType, "package", StringComparison.OrdinalIgnoreCase)
            || !arguments.TryGetValue("reference", out string? reference)
            || !string.Equals(reference, "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", StringComparison.OrdinalIgnoreCase)
            || !arguments.TryGetValue("projectFileExtensions", out string? extensions)
            || !string.Equals(extensions, ".csproj", StringComparison.OrdinalIgnoreCase)
            || !arguments.TryGetValue("version", out string? requiredVersion)
            || !Version.TryParse(requiredVersion, out Version? minimumVersion))
        {
            throw new NotSupportedException($"Template task '{actionId}' or its arguments are not supported.");
        }

        string projectPath = FindSourceProject(directory);
        StringBuilder output = new();
        try
        {
            await _dotnetCli.RunStreamingAsync(
                ["msbuild", projectPath, "-getItem:PackageReference"], directory.Info.FullName,
                line => output.AppendLine(line), null, cancellationToken);
        }
        catch (DotnetCliException exception)
        {
            throw new InvalidDataException($"MSBuild could not evaluate package references: {exception.Message}", exception);
        }

        using JsonDocument document = ParseMsBuildOutput(output.ToString(), "MSBuild returned invalid package-reference data.");
        if (!document.RootElement.TryGetProperty("Items", out JsonElement items)
            || !items.TryGetProperty("PackageReference", out JsonElement packages)
            || packages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The evaluated project did not provide its package references.");
        }

        JsonElement[] matches = [.. packages.EnumerateArray().Where(package =>
            package.TryGetProperty("Identity", out JsonElement identity)
            && string.Equals(identity.GetString(), reference, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length != 1
            || !matches[0].TryGetProperty("Version", out JsonElement version)
            || !Version.TryParse(version.GetString(), out Version? currentVersion)
            || currentVersion < minimumVersion)
        {
            throw new NotSupportedException(
                $"The project must already reference '{reference}' at version {requiredVersion} or later; modifying the project is not supported.");
        }

        return false;
    }

    public async Task ExecuteAsync(
        WorkingDirectory directory, Guid actionId, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        if (actionId != _restore || arguments.Count != 0)
        {
            throw new NotSupportedException($"Template task '{actionId}' cannot be executed.");
        }

        string projectPath = FindSourceProject(directory);
        try
        {
            await _dotnetCli.RunAsync(["restore", projectPath], directory.Info.FullName, cancellationToken);
        }
        catch (DotnetCliException exception)
        {
            throw new GracefulException(
                $"The generated project remains, but 'dotnet restore' failed and may have left partial project state: {exception.Message}",
                exception,
                isUserError: true);
        }
    }

    private static string FindSourceProject(WorkingDirectory directory)
    {
        string[] projects = [.. Directory.EnumerateFiles(directory.Info.FullName, "*.csproj", SearchOption.TopDirectoryOnly)];
        return projects.Length == 1
            ? projects[0]
            : throw new NotSupportedException("Exactly one C# source project is required for this template task.");
    }

    private async Task<(string[] Inputs, Dictionary<string, string> Properties, (string Identity, string Version)[] Packages)> EvaluateProjectAsync(
        DotNetSourceProject project,
        CancellationToken cancellationToken)
    {
        StringBuilder output = new();
        try
        {
            await _dotnetCli.RunStreamingAsync(
                [
                    "msbuild",
                    project.ProjectFilePath,
                    "-getProperty:MSBuildAllProjects,TargetFramework,RootNamespace,OutputType,AzureFunctionsVersion",
                    "-getItem:PackageReference"
                ],
                project.WorkingDirectory.Info.FullName,
                line => output.AppendLine(line),
                null,
                cancellationToken);
        }
        catch (DotnetCliException exception)
        {
            throw new InvalidDataException($"MSBuild could not evaluate project '{project.ProjectFilePath}': {exception.Message}", exception);
        }

        using JsonDocument document = ParseMsBuildOutput(output.ToString(), "MSBuild returned invalid project-evaluation data.");
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("Properties", out JsonElement propertiesElement)
            || propertiesElement.ValueKind != JsonValueKind.Object
            || !propertiesElement.TryGetProperty("MSBuildAllProjects", out JsonElement inputsElement)
            || inputsElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("Items", out JsonElement itemsElement)
            || !itemsElement.TryGetProperty("PackageReference", out JsonElement packagesElement)
            || packagesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("MSBuild did not return the evaluated project inputs and package references.");
        }

        string projectPath = Path.GetFullPath(project.ProjectFilePath);
        string[] inputs = [.. inputsElement.GetString()!
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.GetFullPath(path, project.WorkingDirectory.Info.FullName))];
        if (!inputs.Contains(projectPath, StringComparer.OrdinalIgnoreCase))
        {
            inputs = [.. inputs, projectPath];
        }

        inputs = [.. inputs
            .Order(StringComparer.OrdinalIgnoreCase)];
        if (inputs.Length != inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidDataException("MSBuild returned duplicate project input paths.");
        }

        Dictionary<string, string> properties = new(StringComparer.Ordinal);
        foreach (string name in new[] { "TargetFramework", "RootNamespace", "OutputType", "AzureFunctionsVersion" })
        {
            if (propertiesElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                properties[name] = value.GetString() ?? string.Empty;
            }
        }

        (string Identity, string Version)[] packages = [.. packagesElement.EnumerateArray().Select(package =>
        {
            string identity = package.TryGetProperty("Identity", out JsonElement id) ? id.GetString() ?? string.Empty : string.Empty;
            string version = package.TryGetProperty("Version", out JsonElement packageVersion) ? packageVersion.GetString() ?? string.Empty : string.Empty;
            return (identity, version);
        })];
        return (inputs, properties, packages);
    }

    internal bool IsTemplateRouteQualified(string path)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);
            if (root is null || root.Length != 3 || root[1] != ':' || root[2] != '\\')
            {
                return false;
            }

            DriveInfo drive = new(root);
            return drive.IsReady
                && drive.DriveType == DriveType.Fixed
                && (drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)
                    || drive.DriveFormat.Equals("ReFS", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static JsonDocument ParseMsBuildOutput(string output, string message)
    {
        try
        {
            return JsonDocument.Parse(output);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(message, exception);
        }
    }

    private static bool SameEvaluation(
        (string[] Inputs, Dictionary<string, string> Properties, (string Identity, string Version)[] Packages) left,
        (string[] Inputs, Dictionary<string, string> Properties, (string Identity, string Version)[] Packages) right)
    {
        if (!left.Inputs.SequenceEqual(right.Inputs, StringComparer.OrdinalIgnoreCase)
            || left.Properties.Count != right.Properties.Count
            || left.Properties.Any(property =>
                !right.Properties.TryGetValue(property.Key, out string? value)
                || !string.Equals(property.Value, value, StringComparison.Ordinal))
            || left.Packages.Length != right.Packages.Length)
        {
            return false;
        }

        (string Identity, string Version)[] leftPackages = [.. left.Packages
            .OrderBy(package => package.Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Version, StringComparer.Ordinal)];
        (string Identity, string Version)[] rightPackages = [.. right.Packages
            .OrderBy(package => package.Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Version, StringComparer.Ordinal)];
        return leftPackages.Zip(rightPackages).All(pair =>
            string.Equals(pair.First.Identity, pair.Second.Identity, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pair.First.Version, pair.Second.Version, StringComparison.Ordinal));
    }

    private static string? GetCompatibilityDiagnostic(
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<(string Identity, string Version)> packages)
    {
        if (!properties.TryGetValue("TargetFramework", out string? targetFramework)
            || !string.Equals(targetFramework, "net10.0", StringComparison.OrdinalIgnoreCase))
        {
            return "The HTTP template requires a C# isolated-worker project targeting net10.0.";
        }

        if (!properties.TryGetValue("AzureFunctionsVersion", out string? functionsVersion)
            || !string.Equals(functionsVersion, "v4", StringComparison.OrdinalIgnoreCase)
            || !properties.TryGetValue("OutputType", out string? outputType)
            || !string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
        {
            return "The project is not an executable Azure Functions v4 isolated-worker project.";
        }

        if (!properties.TryGetValue("RootNamespace", out string? rootNamespace) || string.IsNullOrWhiteSpace(rootNamespace))
        {
            return "The project does not define an evaluated root namespace.";
        }

        if (!HasPackage(packages, "Microsoft.Azure.Functions.Worker", null)
            || !HasPackage(packages, "Microsoft.Azure.Functions.Worker.Sdk", null))
        {
            return "The project does not contain the required isolated-worker package references.";
        }

        if (!HasPackage(packages, "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", new Version(2, 1, 1)))
        {
            return "The project must already reference 'Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore' at version 2.1.1 or later; modifying the project is not supported.";
        }

        return null;
    }

    private static bool HasPackage(
        IReadOnlyList<(string Identity, string Version)> packages,
        string identity,
        Version? minimumVersion)
    {
        (string Identity, string Version)[] matches = [.. packages.Where(package =>
            string.Equals(package.Identity, identity, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length != 1)
        {
            return false;
        }

        if (minimumVersion is null)
        {
            return !string.IsNullOrWhiteSpace(matches[0].Version);
        }

        return Version.TryParse(matches[0].Version, out Version? version) && version >= minimumVersion;
    }

    private static ProjectTemplateCompatibility Incompatible(string diagnostic) =>
        new(false, diagnostic, new Dictionary<string, string>(), null);
}
