// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

internal sealed record ActionExtensionEntryV2(string Key, ActionExtensionValueV2 Value) : IEquatable<ActionExtensionEntryV2>;
