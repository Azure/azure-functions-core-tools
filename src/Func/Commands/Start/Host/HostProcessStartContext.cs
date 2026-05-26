// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed record HostProcessStartContext(ContentWorkloadInfo HostWorkload, FunctionsProjectHostRunContext HostRunContext, StartCommandOptions Options);
