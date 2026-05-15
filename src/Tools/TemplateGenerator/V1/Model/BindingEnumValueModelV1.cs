// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingEnumValueModelV1(string Value, string Display) : IEquatable<BindingEnumValueModelV1>;
