// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteBlobFailureDetectorTests
{
    [Fact]
    public void SingleLine_WithServerError_And_AzuriteHeader_Detects()
    {
        var detector = new AzuriteBlobFailureDetector();

        bool transitioned = detector.Observe("Status: 500 (Internal Server Error) Server: Azurite-Blob/3.35.0");

        transitioned.Should().BeTrue();
        detector.Detected.Should().BeTrue();
    }

    [Fact]
    public void ServerError_ThenAzuriteHeader_WithinWindow_Detects()
    {
        var detector = new AzuriteBlobFailureDetector();

        detector.Observe("Status: 500 (Internal Server Error)").Should().BeFalse();
        detector.Observe("Headers:").Should().BeFalse();
        bool transitioned = detector.Observe("Server: Azurite-Blob/3.35.0");

        transitioned.Should().BeTrue();
        detector.Detected.Should().BeTrue();
    }

    [Fact]
    public void ServerError_WithoutAzurite_DoesNotDetect()
    {
        var detector = new AzuriteBlobFailureDetector();

        detector.Observe("Status: 500 (Internal Server Error)");
        detector.Observe("Server: SomeOtherService/1.0");

        detector.Detected.Should().BeFalse();
    }

    [Fact]
    public void AzuriteHeader_WithoutServerError_DoesNotDetect()
    {
        var detector = new AzuriteBlobFailureDetector();

        detector.Observe("Server: Azurite-Blob/3.35.0");

        detector.Detected.Should().BeFalse();
    }

    [Fact]
    public void AzuriteHeader_FarAfterServerError_DoesNotDetect()
    {
        var detector = new AzuriteBlobFailureDetector();

        detector.Observe("Status: 500 (Internal Server Error)");
        for (int i = 0; i < 45; i++)
        {
            detector.Observe($"unrelated host log line {i}");
        }

        detector.Observe("Server: Azurite-Blob/3.35.0");

        detector.Detected.Should().BeFalse();
    }

    [Fact]
    public void Observe_ReturnsTrue_OnlyOnTransition()
    {
        var detector = new AzuriteBlobFailureDetector();

        detector.Observe("500 (Internal Server Error) from Azurite-Blob/3.35.0").Should().BeTrue();
        detector.Observe("500 (Internal Server Error) from Azurite-Blob/3.35.0").Should().BeFalse();
    }

    [Fact]
    public void NullOrEmpty_IsIgnored()
    {
        var detector = new AzuriteBlobFailureDetector();

        detector.Observe(null).Should().BeFalse();
        detector.Observe(string.Empty).Should().BeFalse();
        detector.Detected.Should().BeFalse();
    }

    [Fact]
    public void Detects_RepresentativeRequestFailedExceptionBlock()
    {
        var detector = new AzuriteBlobFailureDetector();
        string[] block =
        [
            "There was an error performing a write operation on the Blob Storage Secret Repository.",
            "Azure.RequestFailedException: Service request failed.",
            "Status: 500 (Internal Server Error)",
            "Headers:",
            "Server: Azurite-Blob/3.35.0",
            "x-ms-error-code: InternalError",
        ];

        bool detectedDuringBlock = false;
        foreach (string line in block)
        {
            detectedDuringBlock |= detector.Observe(line);
        }

        detectedDuringBlock.Should().BeTrue();
        detector.Detected.Should().BeTrue();
    }
}
