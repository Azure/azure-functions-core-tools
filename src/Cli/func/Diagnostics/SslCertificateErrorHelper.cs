// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Authentication;

namespace Azure.Functions.Cli.Diagnostics
{
    /// <summary>
    /// Helpers for detecting SSL/TLS certificate validation failures so callers
    /// can surface a clearer message (typically "you may be behind an SSL
    /// inspection proxy — install its root cert") instead of a generic
    /// networking error.
    /// </summary>
    internal static class SslCertificateErrorHelper
    {
        /// <summary>
        /// Walks the exception chain looking for SSL/TLS certificate validation errors.
        /// Returns true for <see cref="AuthenticationException"/> (which covers expired,
        /// untrusted, and otherwise invalid certificates) or any exception whose message
        /// matches a known SSL failure phrase.
        /// </summary>
        public static bool IsSslCertificateException(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                if (current is AuthenticationException)
                {
                    return true;
                }

                if (ContainsSslKeywords(current.Message))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the message contains keywords associated with SSL/TLS certificate failures.
        /// </summary>
        public static bool ContainsSslKeywords(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            return message.IndexOf("CERTIFICATE_VERIFY_FAILED", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("certificate signed by unknown authority", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("The remote certificate is invalid", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("The SSL connection could not be established", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("SSL handshake failed", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("certificate verify failed", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("certificate has expired", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
