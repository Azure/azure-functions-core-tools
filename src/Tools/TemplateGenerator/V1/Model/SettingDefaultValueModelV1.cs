// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record SettingDefaultValueModelV1(
    SettingDefaultValueKindV1 Kind,
    string? StringValue,
    bool? BoolValue,
    long? LongValue,
    EquatableArray<string>? ArrayValue)
    : IEquatable<SettingDefaultValueModelV1>;
