// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Provides command-facing access to a bootstrapped TemplateEngine environment.
/// </summary>
internal sealed class Templater(IEngineEnvironmentSettings settings)
{
    private readonly IEngineEnvironmentSettings _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    private readonly TemplateCreator _creator = new(settings);

    public IEngineEnvironmentSettings Settings => _settings;

    public TemplateCreator Creator => _creator;
}
