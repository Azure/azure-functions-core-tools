// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Native;

/// <summary>
/// Identifies one hash-bound source in the selected package.
/// </summary>
public sealed record NativeTemplateSourceStamp(string TemplateIdentity, string RelativePath, string Sha256);

/// <summary>
/// Describes an original item and its exact internal native append helper.
/// </summary>
public sealed record NativeTemplateAppendMapping(
    NativeTemplateSourceStamp MappingSource,
    NativeTemplateSourceStamp CreateSource,
    string CreateTargetFileName,
    string AppendTargetFileName,
    string HelperIdentity,
    string HelperConfigurationSha256,
    NativeTemplateSourceStamp HelperSource,
    string ExpectedHelperOutput);

/// <summary>
/// Returns source-qualified original validation and append metadata without granting native invocation permission.
/// </summary>
public sealed record NativeItemPolicyDescription(
    bool InternalExpansionOnly,
    NativeTemplateSourceStamp? ValidationSource,
    string? DefaultName,
    TemplateUserPrompt? FunctionNamePrompt,
    IReadOnlyList<TemplateUserPrompt> ParameterValidation,
    NativeTemplateAppendMapping? Append,
    IReadOnlyList<string> Diagnostics);
