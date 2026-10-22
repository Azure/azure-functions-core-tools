// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

internal sealed record FSharpGeneratedFile(string Path, string Sha256);

internal sealed class FSharpCompileUpdatePlan
{
    internal FSharpCompileUpdatePlan(string projectPath, string expectedSha256, IReadOnlyList<string> compileOrder,
        IReadOnlyList<FSharpGeneratedFile> generatedFiles, byte[] updatedBytes, bool changesProject)
    {
        ProjectPath = projectPath;
        ExpectedSha256 = expectedSha256;
        CompileOrder = Array.AsReadOnly<string>([.. compileOrder]);
        GeneratedFiles = Array.AsReadOnly<FSharpGeneratedFile>([.. generatedFiles]);
        UpdatedBytes = updatedBytes;
        ChangesProject = changesProject;
    }

    public string ProjectPath { get; }

    public string ExpectedSha256 { get; }

    public IReadOnlyList<string> CompileOrder { get; }

    public IReadOnlyList<FSharpGeneratedFile> GeneratedFiles { get; }

    internal ReadOnlyMemory<byte> UpdatedBytes { get; }

    public bool ChangesProject { get; }
}

/// <summary>
/// Qualifies and inserts exact generated F# source files before a terminal Program.fs compile item.
/// </summary>
internal sealed class FSharpCompileUpdater(IDotnetCliRunner dotnetCli, FSharpProjectFiles files)
{
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));
    private readonly FSharpProjectFiles _files = files ?? throw new ArgumentNullException(nameof(files));
    private static readonly StringComparer _paths = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Previews a byte-preserving insertion against the selected project and its actual evaluated compile order.
    /// </summary>
    /// <exception cref="ArgumentException">A required input or exact path/digest is invalid.</exception>
    /// <exception cref="InvalidOperationException">The project, compile order or generated output layout is unqualified.</exception>
    /// <exception cref="IOException">A selected project input cannot be read.</exception>
    /// <exception cref="DotnetCliException">MSBuild evaluation failed.</exception>
    public async Task<FSharpCompileUpdatePlan> PlanAsync(string projectPath, string expectedProjectSha256,
        IReadOnlyList<string> evaluatedCompileOrder, IReadOnlyList<FSharpGeneratedFile> generatedFiles, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProjectSha256);
        ArgumentNullException.ThrowIfNull(evaluatedCompileOrder);
        ArgumentNullException.ThrowIfNull(generatedFiles);
        if (!Path.IsPathFullyQualified(projectPath) || Path.GetExtension(projectPath) != ".fsproj" || !IsDigest(expectedProjectSha256))
        {
            throw new ArgumentException("F# compile repair requires an exact absolute .fsproj path and its SHA-256.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        projectPath = Path.GetFullPath(projectPath);
        string directory = Path.GetDirectoryName(projectPath)!;
        string[] compileOrder = [.. evaluatedCompileOrder];
        FSharpGeneratedFile[] generated = [.. generatedFiles];
        Require(compileOrder.Length > 0 && generated.Length > 0, "F# compile repair requires explicit compile items and generated files.");
        Require(compileOrder.All(path => IsRootFile(path, directory, ".fs"))
            && compileOrder.Distinct(_paths).Count() == compileOrder.Length
            && _paths.Equals(compileOrder[^1], Path.Combine(directory, "Program.fs")),
            "F# compile repair requires unique explicit root compile files ending in Program.fs.");
        Require(generated.All(file => file is not null && IsRootFile(file.Path, directory, ".fs") && IsDigest(file.Sha256)
            && !_paths.Equals(file.Path, Path.Combine(directory, "Program.fs")))
            && generated.Select(file => file.Path).Distinct(_paths).Count() == generated.Length,
            "F# compile repair requires unique exact generated root .fs files, never Program.fs.");
        _files.ValidatePath(projectPath, directory, mustExist: true);
        byte[] original = await _files.ReadAsync(projectPath, cancellationToken);
        Require(Digest(original).Equals(expectedProjectSha256, StringComparison.OrdinalIgnoreCase),
            "The selected F# project does not match its expected source digest.");
        foreach (string path in compileOrder)
        {
            _files.ValidatePath(path, directory, mustExist: true);
        }

        foreach (FSharpGeneratedFile file in generated)
        {
            _files.ValidatePath(file.Path, directory, mustExist: false);
        }

        XElement[] compile;
        string text;
        bool bom = original.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        try
        {
            text = new UTF8Encoding(false, true).GetString(original.AsSpan(bom ? 3 : 0));
            Require(!text.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\r'),
                "F# compile repair supports only LF or CRLF project line endings.");
            using var input = new StringReader(text);
            using var reader = XmlReader.Create(input, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            XElement root = document.Root ?? throw new InvalidOperationException("F# compile repair requires a project document.");
            Require(root.Name == "Project" && (string?)root.Attribute("Sdk") == "Microsoft.NET.Sdk",
                "F# compile repair requires the selected SDK-style project.");
            compile = [.. root.Descendants("Compile")];
            Require(compile.Length == compileOrder.Length && compile.All(item => item.Parent?.Name == "ItemGroup"
                && item.Parent.Parent == root && !item.Parent.HasAttributes
                && item.Attributes().Count() == 1 && item.Attribute("Include") is not null && !item.HasElements
                && string.IsNullOrWhiteSpace(item.Value)),
                "F# compile repair supports only direct unconditional literal Compile Include items without metadata.");
            string[] authored = [.. compile.Select(item => (string)item.Attribute("Include")!)];
            Require(authored.All(IsLiteralFileName) && authored.Select(path => Path.Combine(directory, path)).SequenceEqual(compileOrder, _paths),
                "The explicit F# project Compile items must match the selected evaluated order.");
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("The selected F# project XML is not qualified.", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException("F# compile repair requires valid UTF-8 project content.", exception);
        }

        await ValidateEvaluationAsync(projectPath, compileOrder, cancellationToken);
        _files.ValidatePath(projectPath, directory, mustExist: true);
        Require(Digest(await _files.ReadAsync(projectPath, cancellationToken)).Equals(expectedProjectSha256, StringComparison.OrdinalIgnoreCase),
            "The selected F# project changed during compile repair preview.");
        FSharpGeneratedFile[] missing = [.. generated.Where(file => !compileOrder.Contains(file.Path, _paths))];
        Require(missing.Length == 0 || missing.Length == generated.Length,
            "F# compile repair refuses a partially included generated file set.");
        if (missing.Length == 0)
        {
            Require(compileOrder.TakeLast(generated.Length + 1).SequenceEqual(
                generated.Select(file => file.Path).Append(Path.Combine(directory, "Program.fs")), _paths),
                "Generated F# files already included in the project must retain the qualified order.");
            return new(projectPath, expectedProjectSha256, compileOrder, generated, original, changesProject: false);
        }

        IXmlLineInfo location = compile[^1];
        string[] lines = text.Split('\n');
        int lineStart = lines.Take(location.LineNumber - 1).Sum(line => line.Length + 1);
        string lineText = lines[location.LineNumber - 1];
        int elementStart = lineText.IndexOf("<Compile", StringComparison.Ordinal);
        Require(elementStart >= 0 && lineText[..elementStart].All(character => character is ' ' or '\t')
            && lineText[(elementStart + 1)..].StartsWith("Compile", StringComparison.Ordinal),
            "The terminal Program.fs compile item must occupy its own indented line.");
        string indentation = lineText[..elementStart];
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string insertion = string.Concat(missing.Select(file =>
            indentation + "<Compile " + new XAttribute("Include", Path.GetFileName(file.Path)) + " />" + newline));
        byte[] encoded = Encoding.UTF8.GetBytes(text.Insert(lineStart, insertion));
        byte[] updated = bom ? [0xef, 0xbb, 0xbf, .. encoded] : encoded;
        return new(projectPath, expectedProjectSha256, compileOrder, generated, updated, changesProject: true);
    }

    /// <summary>
    /// Applies a qualified update only after exact native-generated source files exist and project evaluation still matches.
    /// </summary>
    /// <exception cref="ArgumentNullException">The plan is null.</exception>
    /// <exception cref="InvalidOperationException">A selected source, path, project or compile order changed.</exception>
    /// <exception cref="IOException">A selected input or the atomic project update failed.</exception>
    /// <exception cref="DotnetCliException">MSBuild reevaluation failed.</exception>
    public async Task ApplyAsync(FSharpCompileUpdatePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(plan.ProjectPath)!;
        _files.ValidatePath(plan.ProjectPath, directory, mustExist: true);
        byte[] current = await _files.ReadAsync(plan.ProjectPath, cancellationToken);
        Require(Digest(current).Equals(plan.ExpectedSha256, StringComparison.OrdinalIgnoreCase),
            "The selected F# project changed after compile repair preview.");
        foreach (string path in plan.CompileOrder)
        {
            _files.ValidatePath(path, directory, mustExist: true);
        }

        foreach (FSharpGeneratedFile file in plan.GeneratedFiles)
        {
            _files.ValidatePath(file.Path, directory, mustExist: true);
            byte[] bytes = await _files.ReadAsync(file.Path, cancellationToken);
            Require(Digest(bytes).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase),
                "A generated F# source changed after its native output was qualified.");
        }

        await ValidateEvaluationAsync(plan.ProjectPath, plan.CompileOrder, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (FSharpGeneratedFile file in plan.GeneratedFiles)
        {
            _files.ValidatePath(file.Path, directory, mustExist: true);
            Require(Digest(await _files.ReadAsync(file.Path, cancellationToken)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase),
                "A generated F# source changed during compile repair evaluation.");
        }

        _files.ValidatePath(plan.ProjectPath, directory, mustExist: true);
        Require(Digest(await _files.ReadAsync(plan.ProjectPath, cancellationToken)).Equals(plan.ExpectedSha256, StringComparison.OrdinalIgnoreCase),
            "The selected F# project changed during compile repair application.");
        if (plan.ChangesProject)
        {
            await _files.ReplaceAsync(plan.ProjectPath, plan.ExpectedSha256, plan.UpdatedBytes, cancellationToken);
        }
    }

    private async Task ValidateEvaluationAsync(string project, IReadOnlyList<string> expectedOrder, CancellationToken cancellationToken)
    {
        StringBuilder output = new();
        await _dotnetCli.RunStreamingAsync(["msbuild", project, "-nologo", "-getProperty:TargetFramework", "-getItem:Compile"],
            Path.GetDirectoryName(project), line => output.AppendLine(line), null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(output.ToString());
            JsonElement root = document.RootElement;
            Require(root.GetProperty("Properties").GetProperty("TargetFramework").GetString() == DotNetTemplateFrameworks.Default,
                "F# compile repair requires the selected evaluated net10.0 project.");
            JsonElement[] items = [.. root.GetProperty("Items").GetProperty("Compile").EnumerateArray()];
            Require(items.Length == expectedOrder.Count && items.Select(item => item.GetProperty("FullPath").GetString()!)
                .SequenceEqual(expectedOrder, _paths)
                && items.All(item => _paths.Equals(item.GetProperty("DefiningProjectFullPath").GetString(), project)),
                "F# compile repair refuses changed, imported or ambiguous evaluated Compile items.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("F# compile repair could not parse the current MSBuild evaluation.", exception);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidOperationException("F# compile repair could not qualify the current MSBuild evaluation.", exception);
        }
    }

    private static bool IsRootFile(string path, string directory, string extension) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && Path.GetExtension(path) == extension
        && _paths.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory) && IsLiteralFileName(Path.GetFileName(path));

    private static bool IsLiteralFileName(string name) => !string.IsNullOrWhiteSpace(name) && name is not "." and not ".."
        && name.IndexOfAny(['/', '\\', ':', '*', '?', ';', '$', '%', '@', '<', '>', '"', '|']) < 0 && Path.GetExtension(name) == ".fs";

    private static bool IsDigest(string? digest) => digest is { Length: 64 } && digest.All(Uri.IsHexDigit);

    private static string Digest(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
