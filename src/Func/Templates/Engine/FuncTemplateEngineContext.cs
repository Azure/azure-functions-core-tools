// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Resolved project context handed to the template engine for a single
/// <c>func new</c> invocation. Each value is optional; unset values are not
/// surfaced as host params. The engine (and func custom constraints) read
/// these back through
/// <see cref="Microsoft.TemplateEngine.Abstractions.ITemplateEngineHost.TryGetHostParamDefault(string, out string?)"/>
/// using the keys in <see cref="FuncTemplateEngineHostParameters"/>.
/// </summary>
/// <param name="Stack">Resolved stack (e.g. <c>dotnet</c>, <c>node</c>, <c>python</c>).</param>
/// <param name="Language">Resolved language (e.g. <c>C#</c>, <c>typescript</c>).</param>
/// <param name="Bundle">Resolved extension-bundle version the project targets.</param>
internal sealed record FuncTemplateEngineContext(string? Stack, string? Language, string? Bundle);
