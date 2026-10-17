// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Projects;

namespace Azure.Functions.Cli.Templates.Go;

internal sealed class GoProjectWorkflowProvider(GoNativeProjectPreparation preparation) : INativeProjectWorkflowProvider
{
    private readonly GoNativeProjectPreparation _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));

    public string Stack => "go";

    public INativeProjectPreparation Preparation => _preparation;

    public bool Handles(TemplateCatalogEntry entry) => OwnedProjectPreparationSource.Handles(entry, GoNativeProjectPreparation.Definition);
}
