// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record ResourceCultureModelV1
{
    /// <summary>Canonical culture name (e.g. "en", "de-DE").</summary>
    public required string Culture { get; init; }

    public required EquatableArray<ResourceEntryModelV1> Entries { get; init; }
}
