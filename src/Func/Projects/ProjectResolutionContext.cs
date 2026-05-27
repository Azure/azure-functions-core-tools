// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Inputs to project resolution.
/// </summary>
/// <param name="WorkingDirectory">The directory the command is operating on.</param>
internal sealed record ProjectResolutionContext(WorkingDirectory WorkingDirectory);
