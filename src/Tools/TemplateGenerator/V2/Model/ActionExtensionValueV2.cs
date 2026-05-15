// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

internal sealed record ActionExtensionValueV2(
    ActionExtensionValueKindV2 Kind,
    string? StringValue,
    bool? BoolValue,
    double? NumberValue,
    EquatableArray<string>? ArrayValue)
    : IEquatable<ActionExtensionValueV2>;
