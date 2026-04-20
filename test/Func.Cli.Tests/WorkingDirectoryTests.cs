// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Tests that change Directory.SetCurrentDirectory must run serially
/// to avoid cwd races. Mark test classes with [Collection(nameof(WorkingDirectoryTests))].
/// </summary>
[CollectionDefinition(nameof(WorkingDirectoryTests), DisableParallelization = true)]
public class WorkingDirectoryTests;
