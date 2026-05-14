// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Metadata about the current host run that is not derived from the log stream.
/// </summary>
internal sealed record DashboardRunInfo(
    string CliVersion = "n/a",
    string ProfileName = "none",
    string StackName = "unknown");
