// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

internal sealed record TemplateGeneratedFSharpFile(string Path, string Sha256, string? ExpectedTargetSha256 = null);

/// <summary>
/// Pins a selected F# project and the native-generated sources for an explicitly composed compile-order repair.
/// </summary>
internal sealed record TemplateFSharpCompileEffect(
    TemplateEffectSource Source,
    string ProjectPath,
    string ExpectedProjectSha256,
    IReadOnlyList<string> EvaluatedCompileOrder,
    IReadOnlyList<TemplateGeneratedFSharpFile> GeneratedFiles) : TemplateEffect(Source, ProjectPath)
{
    public IReadOnlyList<string> EvaluatedCompileOrder { get; } =
        Array.AsReadOnly<string>([.. EvaluatedCompileOrder ?? throw new ArgumentNullException(nameof(EvaluatedCompileOrder))]);

    public IReadOnlyList<TemplateGeneratedFSharpFile> GeneratedFiles { get; } =
        Array.AsReadOnly<TemplateGeneratedFSharpFile>([.. GeneratedFiles ?? throw new ArgumentNullException(nameof(GeneratedFiles))]);
}
