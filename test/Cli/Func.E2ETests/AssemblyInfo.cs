// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

// Configure xUnit to run test collections in parallel
// Tests within the same collection (e.g., tests sharing a fixture) will still run sequentially
// Using 8 threads for faster CI execution (matches dotnet test -m:8)
[assembly: CollectionBehavior(DisableTestParallelization = false, MaxParallelThreads = 8)]
