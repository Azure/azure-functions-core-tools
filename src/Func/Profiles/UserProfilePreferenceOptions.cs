// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Represents user-level profile preferences.
/// </summary>
internal sealed class UserProfilePreferenceOptions
{
    public string? DefaultProfile { get; set; }
}
