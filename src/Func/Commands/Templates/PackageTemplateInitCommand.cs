// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;

namespace Azure.Functions.Cli.Commands.Templates;

internal sealed class PackageTemplateInitCommand(PackageTemplateApplication application, PackageTemplateCommandExecutor executor)
    : PackageTemplateCommand("init", "Create a project from an exact local package in the isolated candidate graph.", TemplateType.Project, application, executor);
