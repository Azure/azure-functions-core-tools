// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Templates.Native;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Node.Native;

/// <summary>
/// Qualifies and completes the owning Node workload's fixed dependency preparation.
/// </summary>
internal sealed class NodeProjectPreparation(NodeNpmInstaller npmInstaller) : INativeTemplateProjectPreparation
{
    private const string PackageId = "Azure.Functions.Templates.Node.Basic";
    private const string ManifestPath = "provenance/node/compatibility.json";
    private readonly NodeNpmInstaller _npmInstaller = npmInstaller ?? throw new ArgumentNullException(nameof(npmInstaller));
    private readonly NodeProjectSource _projects = new();

    internal static bool RequiresPreparation(NativeTemplateSource selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return selected.IsProject && selected.PackageId == PackageId;
    }

    public async Task<NativeTemplateProjectPreparationPlan> PreviewAsync(
        NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);
        cancellationToken.ThrowIfCancellationRequested();
        await captured.RevalidateAsync(cancellationToken);
        NativeTemplateSource source = captured.Invocation.Source;
        if (!RequiresPreparation(source) || source.Language is not ("JavaScript" or "TypeScript")
            || source.Identity != "Azure.Functions.Node.Basic." + source.Language
            || !captured.Invocation.CanonicalValues.TryGetValue("SkipNpmInstall", out string? value)
            || !bool.TryParse(value, out bool skip))
        {
            return new(false, false, ["Node preparation requires the selected basic project and its canonical SkipNpmInstall value."]);
        }
        await _projects.ReadAsync(source, cancellationToken);
        await ValidateOriginalProjectAsync(source, cancellationToken);
        string root = captured.Invocation.OutputDirectory.Info.FullName;
        if (captured.PrimaryOutputs.Count != 1
            || Path.GetFullPath(captured.PrimaryOutputs[0], root) != Path.Combine(root, "host.json")
            || !HasFile(captured, "host.json") || !HasFile(captured, "package.json"))
        {
            return new(false, false, ["Node preparation requires the actual basic project image and literal host.json output."]);
        }
        await captured.RevalidateAsync(cancellationToken);
        return new(true, skip, []);
    }

    public async Task<NativeTemplateProjectPreparationResult> CompleteAsync(
        NativeTemplateCreatedProject created, NativeTemplateProjectPreparationPlan preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        await created.RevalidateAsync(cancellationToken);
        NativeTemplateProjectPreparationPlan current = await PreviewAsync(created.Output, cancellationToken);
        if (!preview.Succeeded || !current.Succeeded || current.Skipped != preview.Skipped)
        {
            return new(false, ["Node preparation changed after preview. The generated project was retained.", .. current.Diagnostics]);
        }
        string root = created.Output.Invocation.OutputDirectory.Info.FullName;
        string relative = Path.GetRelativePath(root, created.ConfigurationPath);
        if (!Path.IsPathFullyQualified(created.ConfigurationPath) || relative == ".." || Path.IsPathRooted(relative)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return new(false, ["Node preparation requires the host-issued configuration within the selected project."]);
        }
        using JsonDocument configuration = Parse(await created.ReadConfigurationAsync(cancellationToken));
        JsonElement stack = Property(configuration.RootElement, "stack");
        if (Text(stack, "runtime") != "node" || Text(stack, "language") != created.Output.Invocation.Source.Language)
        {
            return new(false, ["Mandatory CLI configuration does not match the selected Node project."]);
        }
        await created.RevalidateAsync(cancellationToken);
        if (current.Skipped)
        {
            return new(true, ["Skipped npm installation."]);
        }
        (int exitCode, string stderr) = await _npmInstaller.InstallRequiredAsync(root, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string[] diagnostics = string.IsNullOrWhiteSpace(stderr) ? [] : [stderr];
        return exitCode == 0 ? new(true, diagnostics, TargetMayHaveChanged: true)
            : new(false, [$"'npm install' failed (exit {exitCode}). The generated project and CLI configuration were retained.", .. diagnostics],
                TargetMayHaveChanged: true);
    }

    private static bool HasFile(NativeTemplateCapturedOutput captured, string name)
        => captured.Files.Count(file => Path.GetFullPath(file.TargetRelativePath, captured.Invocation.OutputDirectory.Info.FullName)
            == Path.Combine(captured.Invocation.OutputDirectory.Info.FullName, name)) == 1;

    private static async Task ValidateOriginalProjectAsync(NativeTemplateSource source, CancellationToken cancellationToken)
    {
        using JsonDocument manifest = Parse(await source.ReadPackageFileAsync(ManifestPath, cancellationToken));
        JsonElement root = manifest.RootElement;
        if (Text(root, "packageId") != source.PackageId
            || !NuGetVersion.TryParse(Text(root, "packageVersion"), out NuGetVersion? declared)
            || !NuGetVersion.TryParse(source.PackageVersion, out NuGetVersion? actual) || declared != actual)
        {
            throw new InvalidDataException("Node preparation provenance differs from the selected package.");
        }
        JsonElement mappings = Property(root, "projects");
        if (mappings.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Node preparation requires an array of original project mappings.");
        }
        JsonElement[] selected = [.. mappings.EnumerateArray().Where(mapping => Text(mapping, "identity") == source.Identity)];
        if (selected.Length != 1)
        {
            throw new InvalidDataException("The selected Node project has no unique original source mapping.");
        }
        string folder = source.Language == "JavaScript" ? "project-js" : "project-ts";
        foreach ((string role, string path) in new[]
        {
            ("configuration", $"content/{folder}/.template.config/template.json"),
            ("host", $"content/{folder}/.template.config/func.host.json"),
            ("package", $"content/{folder}/package.json"),
        })
        {
            JsonElement descriptor = Property(selected[0], role);
            if (Text(descriptor, "path") != path)
            {
                throw new InvalidDataException("Node preparation requires its exact selected project source paths.");
            }
            ReadOnlyMemory<byte> bytes = await source.ReadPackageFileAsync(path, cancellationToken);
            string hash = Convert.ToHexString(SHA256.HashData(bytes.Span));
            if (!string.Equals(Text(descriptor, "sha256"), hash, StringComparison.OrdinalIgnoreCase)
                || role == "configuration" && !string.Equals(hash, source.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The selected Node project preparation source changed.");
            }
        }
    }

    private static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)
            ? property : throw new InvalidDataException($"Node preparation source is missing '{name}'.");

    private static string Text(JsonElement value, string name)
        => Property(value, name) is { ValueKind: JsonValueKind.String } property && property.GetString() is { } text
            ? text : throw new InvalidDataException($"Node preparation source '{name}' must be a string.");

    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            bool bom = bytes.Length >= 3 && bytes.Span[0] == 0xef && bytes.Span[1] == 0xbb && bytes.Span[2] == 0xbf;
            return JsonDocument.Parse(bom ? bytes[3..] : bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The selected Node preparation source contains malformed JSON.", exception);
        }
    }
}
