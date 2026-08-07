// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Defines func-owned TemplateEngine host parameter names.
/// </summary>
internal static class FuncTemplateEngineHostParameters
{
    public const string Stack = "func:stack";

    public const string Language = "func:language";

    public const string BundleId = "func:bundle-id";

    public const string BundleVersion = "func:bundle-version";
}
