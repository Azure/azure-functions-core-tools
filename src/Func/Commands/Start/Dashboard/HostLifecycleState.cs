// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Lifecycle states the dashboard exposes for the in-process host. Mirrors
/// the values used by the well-known <c>host.state</c> attribute.
/// </summary>
internal enum HostLifecycleState
{
    Starting,
    Ready,
    Recycling,
    Stopped,
}
