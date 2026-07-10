// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Host-parameter keys under which resolved project context
/// (<see cref="FuncTemplateEngineContext"/>) is exposed to
/// <c>Microsoft.TemplateEngine</c>. The <c>func:</c> prefix keeps them from
/// colliding with the engine's built-in host params (e.g. <c>prefs:language</c>).
/// Func custom constraints read these values back via
/// <see cref="Microsoft.TemplateEngine.Abstractions.ITemplateEngineHost.TryGetHostParamDefault(string, out string?)"/>.
/// </summary>
internal static class FuncTemplateEngineHostParameters
{
    /// <summary>Resolved stack (e.g. <c>dotnet</c>, <c>node</c>, <c>python</c>).</summary>
    public const string Stack = "func:stack";

    /// <summary>Resolved language (e.g. <c>C#</c>, <c>typescript</c>).</summary>
    public const string Language = "func:language";

    /// <summary>Resolved extension-bundle version the project targets.</summary>
    public const string Bundle = "func:bundle";
}
