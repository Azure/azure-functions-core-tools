// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Profile lifecycle metadata.
/// </summary>
internal enum ProfileStatus
{
    Stable,
    Preview,
    Deprecated,
}
