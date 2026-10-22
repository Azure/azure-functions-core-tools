// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Identifies conflicting installed stack IDs or alias ownership.
/// </summary>
internal sealed class ConflictingProjectStackMetadataException(string message) : InvalidOperationException(message);
