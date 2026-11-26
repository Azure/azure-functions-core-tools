// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

internal sealed class NuGetResourceUnavailableException(string message) : InvalidOperationException(message);
