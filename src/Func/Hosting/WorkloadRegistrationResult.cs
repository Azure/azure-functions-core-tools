// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Boot count and duration returned by
/// <see cref="WorkloadRegistration.RegisterWorkloadsAsync"/> so the caller
/// can emit telemetry after <see cref="Microsoft.Extensions.Hosting.IHost.StartAsync"/>
/// subscribes the OpenTelemetry pipeline.
/// </summary>
internal readonly record struct WorkloadRegistrationResult(int WorkloadCount, long ElapsedMilliseconds);
