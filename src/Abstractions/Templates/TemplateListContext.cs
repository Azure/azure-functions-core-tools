// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Input to <see cref="ITemplateEngineProvider.ListTemplatesAsync"/>. The
/// orchestrator already resolved the project, profile, stack, and language
/// before calling the provider, so providers receive a fully-narrowed query.
/// </summary>
/// <param name="WorkingDirectory">The resolved project working directory.</param>
/// <param name="Stack">
/// Canonical active stack id (e.g. <c>"node"</c>). Providers should return only
/// templates whose owning stack matches this value.
/// </param>
/// <param name="Language">
/// Resolved language id (e.g. <c>"javascript"</c>, <c>"csharp"</c>). Read from
/// <c>.func/config.json</c> <c>stack.language</c> via
/// <c>IOptionsMonitor&lt;StackOptions&gt;</c>; substituted to the stack's
/// canonical single-language constant for single-language stacks. <c>null</c>
/// only in defensive code paths where the runner could not settle a language.
/// </param>
public sealed record TemplateListContext(
    WorkingDirectory WorkingDirectory,
    string Stack,
    string? Language);
