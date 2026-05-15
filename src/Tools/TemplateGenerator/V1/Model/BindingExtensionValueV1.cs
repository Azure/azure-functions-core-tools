// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingExtensionValueV1(BindingExtensionValueKindV1 Kind, string? StringValue, bool? BoolValue, EquatableArray<string>? ArrayValue)
    : IEquatable<BindingExtensionValueV1>;
