// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Runtime status of a discovered function as the CLI sees it. Drives the
/// status pill in the compact header and the <c>summary</c> totals in JSON
/// output.
/// </summary>
internal enum FunctionStatus
{
    Ready,
    Active,
    Error,
}
