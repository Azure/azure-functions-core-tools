// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Schema identifiers supported by this CLI.
/// </summary>
internal static class ProfileSchemas
{
    public const string BuiltInRegistryV1 = "https://aka.ms/func-profiles/v1/schema.json";
    public const string CustomProfilesV1 = "https://aka.ms/func-custom-profiles/v1/schema.json";
    public const string ProjectConfigV1 = "https://aka.ms/func-config/v1/schema.json";
}
