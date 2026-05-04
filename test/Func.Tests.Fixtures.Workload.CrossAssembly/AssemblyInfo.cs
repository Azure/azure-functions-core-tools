// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

// Intentionally points the attribute at IWorkload, a type defined in
// Abstractions rather than this assembly. The scanner must reject this
// because cross-assembly entry points are unsupported.
[assembly: CliWorkload<IWorkload>]
