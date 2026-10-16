// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed record NativeTemplatePreparationSnapshot(
    TemplateEffectSource Source, ICreationEffects Effects, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> ExpandedFiles);

internal sealed record NativeTemplatePreparationResult(NativeTemplatePreparationSnapshot? Snapshot, TemplateInvocationResult? Failure);
