// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;

namespace Azure.Functions.Cli.Commands.Templates;

internal sealed class PackageTemplateNewCommand(PackageTemplateApplication application, PackageTemplateCommandExecutor executor)
    : PackageTemplateCommand("new", "Create a compatible item from an exact local package in the isolated candidate graph.", TemplateType.Item, application, executor);
