// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Inputs to <see cref="ITemplateProvider.ScaffoldAsync"/>. Captures the
/// resolved CLI inputs that every scaffold call needs.
/// </summary>
/// <param name="TemplateName">Selected template id (e.g. "HttpTrigger").</param>
/// <param name="FunctionName">User-chosen function name.</param>
/// <param name="OutputPath">Project directory the function is added to.</param>
/// <param name="Language">Programming language. May be null for runtimes with a single language.</param>
public record FunctionScaffoldContext(
    string TemplateName,
    string FunctionName,
    string OutputPath,
    string? Language);
