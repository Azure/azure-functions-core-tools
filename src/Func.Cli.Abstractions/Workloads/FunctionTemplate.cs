// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>A function template surfaced to the user via <c>func new</c>.</summary>
/// <param name="Name">Template id (e.g. "HttpTrigger"). Unique within a worker runtime.</param>
/// <param name="Description">One-line description shown in template lists.</param>
/// <param name="WorkerRuntime">The worker runtime this template targets.</param>
public record FunctionTemplate(string Name, string Description, string WorkerRuntime);
