// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Tests.Templates.Selection;

internal static class SelectionFixtures
{
    public static TemplateCatalogEntry Metadata(string identity = "Contoso.Item") => new(
        identity, "Example item", null, TemplateType.Item, ["item"], "group", "JavaScript", 0, null, false, [], null);

    public static TemplateParameterDefinition Parameter(
        string name = "Message",
        string dataType = "string",
        string? defaultValue = null,
        bool required = false) => new(name, name, null, null, dataType, [], defaultValue, required, false, false);

    public static TemplateSelectionRequest Request(
        string reference = "item",
        TemplateType type = TemplateType.Item,
        params string[] tokens) => new(reference, type, [.. tokens], []);
}
