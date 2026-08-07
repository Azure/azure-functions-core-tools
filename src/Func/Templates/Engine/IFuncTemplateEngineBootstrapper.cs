// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates a fresh TemplateEngine environment for a command invocation.
/// </summary>
internal interface IFuncTemplateEngineBootstrapper
{
    public IEngineEnvironmentSettings Create(FuncTemplateEngineContext context);
}
