// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http;
using System.Security.Authentication;
using Azure.Functions.Cli.Diagnostics;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ConsoleTests
{
    public class SslCertificateErrorHelperTests
    {
        [Theory]
        [InlineData("CERTIFICATE_VERIFY_FAILED", true)]
        [InlineData("certificate signed by unknown authority", true)]
        [InlineData("The remote certificate is invalid according to the validation procedure", true)]
        [InlineData("The SSL connection could not be established", true)]
        [InlineData("SSL handshake failed", true)]
        [InlineData("certificate verify failed", true)]
        [InlineData("The remote certificate was rejected by the provided RemoteCertificateValidationCallback. The certificate has expired.", true)]
        [InlineData("The operation has timed out.", false)]
        [InlineData("Worker process started and initialized.", false)]
        [InlineData("gRPC channel failed", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ContainsSslKeywords_Tests(string message, bool expected)
        {
            Assert.Equal(expected, SslCertificateErrorHelper.ContainsSslKeywords(message));
        }

        [Fact]
        public void IsSslCertificateException_WithAuthenticationException_ReturnsTrue()
        {
            var exception = new AuthenticationException("The remote certificate is invalid.");
            Assert.True(SslCertificateErrorHelper.IsSslCertificateException(exception));
        }

        [Fact]
        public void IsSslCertificateException_WithNestedAuthenticationException_ReturnsTrue()
        {
            var inner = new AuthenticationException("SSL handshake failed");
            var outer = new Exception("Connection failed", inner);
            Assert.True(SslCertificateErrorHelper.IsSslCertificateException(outer));
        }

        [Fact]
        public void IsSslCertificateException_WithSslKeywordInMessage_ReturnsTrue()
        {
            var exception = new Exception("CERTIFICATE_VERIFY_FAILED: unable to get local issuer certificate");
            Assert.True(SslCertificateErrorHelper.IsSslCertificateException(exception));
        }

        [Fact]
        public void IsSslCertificateException_WithUnrelatedExceptions_ReturnsFalse()
        {
            var exception = new Exception("The operation has timed out.");
            Assert.False(SslCertificateErrorHelper.IsSslCertificateException(exception));
        }

        [Fact]
        public void IsSslCertificateException_WithNull_ReturnsFalse()
        {
            Assert.False(SslCertificateErrorHelper.IsSslCertificateException(null));
        }

        // An expired server certificate surfaces from HttpClient as
        // HttpRequestException wrapping AuthenticationException with a message
        // that includes "certificate has expired". This test pins the chain
        // walk so ExtensionBundleHelper's SSL catch — which marks the bundle
        // offline and falls back to the cached version — fires for this case.
        [Fact]
        public void IsSslCertificateException_WithExpiredCertificate_ReturnsTrue()
        {
            var inner = new AuthenticationException(
                "The remote certificate was rejected by the provided RemoteCertificateValidationCallback. The certificate has expired.");
            var outer = new HttpRequestException("An error occurred while sending the request.", inner);

            Assert.True(SslCertificateErrorHelper.IsSslCertificateException(outer));
        }
    }
}
