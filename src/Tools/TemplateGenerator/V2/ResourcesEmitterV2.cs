// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Tools.TemplateGenerator.Common;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2;

internal static class ResourcesEmitterV2
{
    private const string ResourcesNamespace = "Azure.Functions.Cli.Resources";
    private const string LocalizerName = "KnownResourcesV2Localizer";
    private const string DefaultCulture = "en";

    public static IEnumerable<(string HintName, string Source)> EmitStaticData(ResourcesCatalogModelV2 catalog)
    {
        ResourceCultureModelV2? defaultCulture = null;
        bool hasEnUs = false;
        foreach (ResourceCultureModelV2 c in catalog.Cultures)
        {
            if (string.Equals(c.Culture, DefaultCulture, StringComparison.OrdinalIgnoreCase))
            {
                defaultCulture = c;
            }
            else if (string.Equals(c.Culture, "en-US", StringComparison.OrdinalIgnoreCase))
            {
                hasEnUs = true;
            }
        }

        foreach (ResourceCultureModelV2 culture in catalog.Cultures)
        {
            yield return EmitCulture(culture);
        }

        bool emitEnUsAlias = defaultCulture is not null && !hasEnUs;
        if (emitEnUsAlias)
        {
            yield return EmitEnUsAlias();
        }

        if (defaultCulture is not null)
        {
            yield return EmitTokens(defaultCulture);
        }

        yield return EmitCulturesAggregate(catalog, defaultCulture, emitEnUsAlias);

        if (catalog.Cultures.Length > 0)
        {
            yield return EmitLocalizer();
        }
    }

    private static (string HintName, string Source) EmitEnUsAlias()
    {
        var sb = new StringBuilder(512);
        AppendHeader(sb);
        sb.AppendLine("internal static partial class KnownResources");
        sb.AppendLine("{");
        sb.AppendLine("    internal static partial class V2");
        sb.AppendLine("    {");
        sb.AppendLine("        // en-US is conventionally the same as the default English dictionary;");
        sb.AppendLine("        // alias the lazy instance so en-US lookups share the same materialized dictionary.");
        sb.AppendLine("        private static readonly global::System.Lazy<global::System.Collections.Generic.IReadOnlyDictionary<string, string>> _en_US = _en;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return ("KnownResources.V2.En_US.g.cs", sb.ToString());
    }

    private static (string HintName, string Source) EmitCulture(ResourceCultureModelV2 culture)
    {
        string ident = EmitterHelpers.CultureToIdentifier(culture.Culture);
        string fieldName = EmitterHelpers.ToCamel(ident);
        var sb = new StringBuilder(8192);
        AppendHeader(sb);
        sb.AppendLine("internal static partial class KnownResources");
        sb.AppendLine("{");
        sb.AppendLine("    internal static partial class V2");
        sb.AppendLine("    {");
        sb.Append("        private static readonly global::System.Lazy<global::System.Collections.Generic.IReadOnlyDictionary<string, string>> _")
            .Append(fieldName).AppendLine(" = new(() =>");
        sb.AppendLine("            new global::System.Collections.Generic.Dictionary<string, string>");
        sb.AppendLine("            {");
        foreach (ResourceEntryModelV2 entry in culture.Entries)
        {
            sb.Append("                [").Append(EmitterHelpers.Literal(entry.Key))
                .Append("] = ").Append(EmitterHelpers.Literal(entry.Value)).AppendLine(",");
        }

        sb.AppendLine("            });");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return ("KnownResources.V2." + ident + ".g.cs", sb.ToString());
    }

    private static (string HintName, string Source) EmitTokens(ResourceCultureModelV2 defaultCulture)
    {
        var sb = new StringBuilder(8192);
        AppendHeader(sb);
        sb.AppendLine("internal static partial class KnownResources");
        sb.AppendLine("{");
        sb.AppendLine("    internal static partial class V2");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>Token name constants (the raw keys used in resource lookups).</summary>");
        sb.AppendLine("        internal static class Tokens");
        sb.AppendLine("        {");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ResourceEntryModelV2 entry in defaultCulture.Entries)
        {
            string ident = EmitterHelpers.SnakeToPascal(entry.Key);
            if (string.IsNullOrEmpty(ident) || !seen.Add(ident))
            {
                continue;
            }

            sb.Append("            public const string ").Append(ident)
                .Append(" = ").Append(EmitterHelpers.Literal(entry.Key)).AppendLine(";");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return ("KnownResources.V2.Tokens.g.cs", sb.ToString());
    }

    private static (string HintName, string Source) EmitCulturesAggregate(ResourcesCatalogModelV2 catalog, ResourceCultureModelV2? defaultCulture, bool includeSyntheticEnUs)
    {
        var sb = new StringBuilder(2048);
        AppendHeader(sb);
        sb.AppendLine("internal static partial class KnownResources");
        sb.AppendLine("{");
        sb.AppendLine("    internal static partial class V2");
        sb.AppendLine("    {");

        sb.AppendLine("        /// <summary>Singleton <see cref=\"global::Microsoft.Extensions.Localization.IStringLocalizer\"/> backed by the generated v2 resource dictionaries.</summary>");
        sb.AppendLine("        public static readonly global::Microsoft.Extensions.Localization.IStringLocalizer Localizer = global::Azure.Functions.Cli.Resources.V2.KnownResourcesV2Localizer.Instance;");
        sb.AppendLine();

        sb.AppendLine("        /// <summary>The default culture's dictionary (English), lazily materialized on first access.</summary>");
        if (defaultCulture is not null)
        {
            string fieldName = EmitterHelpers.ToCamel(EmitterHelpers.CultureToIdentifier(defaultCulture.Culture));
            sb.Append("        public static global::System.Collections.Generic.IReadOnlyDictionary<string, string> Default => _")
                .Append(fieldName).AppendLine(".Value;");
        }
        else
        {
            sb.AppendLine("        private static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, string> _emptyDefault = new global::System.Collections.Generic.Dictionary<string, string>();");
            sb.AppendLine("        public static global::System.Collections.Generic.IReadOnlyDictionary<string, string> Default => _emptyDefault;");
        }

        sb.AppendLine();
        sb.AppendLine("        /// <summary>Looks up a culture's resource dictionary by <see cref=\"global::System.Globalization.CultureInfo.Name\"/>. The dictionary is materialized on first request.</summary>");
        sb.AppendLine("        public static bool TryGetCulture(global::System.Globalization.CultureInfo culture, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out global::System.Collections.Generic.IReadOnlyDictionary<string, string>? value)");
        sb.AppendLine("            => TryGetCulture(culture.Name, out value);");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>Looks up a culture's resource dictionary by name (canonical culture name, e.g. \"en\", \"de-DE\"). The dictionary is materialized on first request.</summary>");
        sb.AppendLine("        public static bool TryGetCulture(string culture, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out global::System.Collections.Generic.IReadOnlyDictionary<string, string>? value)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (culture)");
        sb.AppendLine("            {");
        var emittedCases = new HashSet<string>(StringComparer.Ordinal);
        foreach (ResourceCultureModelV2 culture in catalog.Cultures)
        {
            if (!emittedCases.Add(culture.Culture))
            {
                continue;
            }

            string fieldName = EmitterHelpers.ToCamel(EmitterHelpers.CultureToIdentifier(culture.Culture));
            sb.Append("                case ").Append(EmitterHelpers.Literal(culture.Culture))
                .Append(": value = _").Append(fieldName).AppendLine(".Value; return true;");
        }

        if (includeSyntheticEnUs && emittedCases.Add("en-US"))
        {
            sb.AppendLine("                case \"en-US\": value = _en_US.Value; return true;");
        }

        sb.AppendLine("                default: value = null; return false;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return ("KnownResources.V2.g.cs", sb.ToString());
    }

    private static (string HintName, string Source) EmitLocalizer()
    {
        string source = $$"""
            // <auto-generated/>
            #nullable enable

            namespace {{ResourcesNamespace}}.V2;

            using global::Microsoft.Extensions.Localization;
            using global::System.Collections.Generic;
            using global::System.Globalization;

            /// <summary>
            /// <see cref="IStringLocalizer"/> implementation backed by the generated v2 resource dictionaries.
            /// Lookup order: <see cref="CultureInfo.CurrentUICulture"/>, then its parent cultures, then "en".
            /// </summary>
            internal sealed class {{LocalizerName}} : IStringLocalizer
            {
                /// <summary>Singleton instance of the localizer.</summary>
                public static readonly {{LocalizerName}} Instance = new();

                public LocalizedString this[string name] => Lookup(name, formatArgs: null);

                public LocalizedString this[string name, params object[] arguments] => Lookup(name, arguments);

                public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
                {
                    var seen = new HashSet<string>(global::System.StringComparer.Ordinal);
                    foreach (var dict in EnumerateCultureDictionaries(includeParentCultures))
                    {
                        foreach (var kvp in dict)
                        {
                            if (seen.Add(kvp.Key))
                            {
                                yield return new LocalizedString(kvp.Key, kvp.Value, resourceNotFound: false);
                            }
                        }
                    }
                }

                private static LocalizedString Lookup(string name, object[]? formatArgs)
                {
                    foreach (var dict in EnumerateCultureDictionaries(includeParentCultures: true))
                    {
                        if (dict.TryGetValue(name, out var value))
                        {
                            string formatted = formatArgs is { Length: > 0 }
                                ? string.Format(CultureInfo.CurrentUICulture, value, formatArgs)
                                : value;
                            return new LocalizedString(name, formatted, resourceNotFound: false);
                        }
                    }

                    return new LocalizedString(name, name, resourceNotFound: true);
                }

                private static IEnumerable<IReadOnlyDictionary<string, string>> EnumerateCultureDictionaries(bool includeParentCultures)
                {
                    var culture = CultureInfo.CurrentUICulture;
                    if (includeParentCultures)
                    {
                        while (!culture.Equals(CultureInfo.InvariantCulture))
                        {
                            if (global::Azure.Functions.Cli.Resources.KnownResources.V2.TryGetCulture(culture, out var dict))
                            {
                                yield return dict;
                            }

                            culture = culture.Parent;
                        }
                    }
                    else
                    {
                        if (global::Azure.Functions.Cli.Resources.KnownResources.V2.TryGetCulture(culture, out var dict))
                        {
                            yield return dict;
                        }
                    }

                    yield return global::Azure.Functions.Cli.Resources.KnownResources.V2.Default;
                }
            }

            """;
        return ("KnownResourcesV2Localizer.g.cs", source);
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.Append("namespace ").Append(ResourcesNamespace).AppendLine(";");
        sb.AppendLine();
    }
}
