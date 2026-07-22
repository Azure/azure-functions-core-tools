// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Convenience helpers for SHA256 hashing.
/// </summary>
internal static class Sha256Helpers
{
    /// <summary>
    /// Computes the SHA-256 hash of the UTF-8 encoded content and returns it as a lowercase hex string.
    /// </summary>
    public static string HashDataLowerHex(string content)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }
}
