// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Watches host output for a 500 (Internal Server Error) coming from the CLI's
/// managed Azurite instance. The signal is a server error paired with the
/// <c>Azurite-Blob</c> server header, which only the local emulator emits, so
/// pairing the two avoids firing on unrelated 500s. State is rolling because
/// the two markers usually arrive on separate host log lines within one
/// <c>RequestFailedException</c> dump.
/// </summary>
internal sealed class AzuriteBlobFailureDetector
{
    private const string ServerErrorMarker = "Internal Server Error";
    private const string AzuriteMarker = "Azurite-Blob";

    // How many lines a seen server error stays "hot" waiting for the
    // Azurite-Blob header before we assume the two are unrelated. Generous
    // because the exception dump can carry many header lines in between.
    private const int ServerErrorWindow = 40;

    private int _serverErrorCountdown;

    /// <summary>
    /// True once a managed-Azurite 500 has been observed. Latches on and never
    /// resets, so callers act on it exactly once.
    /// </summary>
    public bool Detected { get; private set; }

    /// <summary>
    /// Feeds one piece of host output to the detector. Returns <c>true</c> only
    /// on the transition into the detected state so callers can act once.
    /// </summary>
    public bool Observe(string? text)
    {
        if (Detected || string.IsNullOrEmpty(text))
        {
            return false;
        }

        bool hasServerError = text.Contains(ServerErrorMarker, StringComparison.OrdinalIgnoreCase);
        bool hasAzurite = text.Contains(AzuriteMarker, StringComparison.OrdinalIgnoreCase);

        // Same line carries both, or a recent server error is now followed by
        // the Azurite-Blob header.
        if (hasAzurite && (hasServerError || _serverErrorCountdown > 0))
        {
            Detected = true;
            return true;
        }

        if (hasServerError)
        {
            _serverErrorCountdown = ServerErrorWindow;
        }
        else if (_serverErrorCountdown > 0)
        {
            _serverErrorCountdown--;
        }

        return false;
    }
}
