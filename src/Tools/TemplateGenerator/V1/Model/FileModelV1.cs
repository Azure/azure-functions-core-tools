// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record FileModelV1(string Name, string Content) : IEquatable<FileModelV1>;
