// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

internal sealed record FileModelV2(string Name, string Content) : IEquatable<FileModelV2>;
