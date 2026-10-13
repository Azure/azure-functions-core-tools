// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Uses OS-owned file handles and writer tickets to prevent readers from overtaking queued writers.
/// </summary>
internal sealed class FileTemplateStoreLock : ITemplateStoreLock
{
    public async Task<IDisposable> AcquireAsync(string directory, TemplateStoreAccess access, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("The template lock directory must be absolute.", nameof(directory));
        }

        if (!Enum.IsDefined(access))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        string owners = Path.Combine(directory, "owners.lock");
        using FileStream? intent = access == TemplateStoreAccess.Write ? await AcquireWriterIntentAsync(directory, cancellationToken) : null;
        TemplateStoreWaitReason? lastWait = null;
        void ReportWait(TemplateStoreWaitReason reason)
        {
            if (lastWait != reason)
            {
                waiting?.Invoke(reason);
                lastWait = reason;
            }
        }

        // Announce writers before competing for the short admission gate, so reader churn cannot starve ticket registration.
        // Creation is serialized separately from shared-read admission.
        using (FileStream? gate = await AcquireGateAsync(directory, access, cancellationToken, ReportWait))
        {
            using FileStream? probe = TryOpen(Path.Combine(directory, "gate.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (probe is not null)
            {
                throw new PlatformNotSupportedException("The template-store filesystem does not enforce exclusive file ownership. Use a CLI home on a filesystem that supports locking.");
            }

            if (!File.Exists(owners))
            {
                using FileStream initialization = new(owners, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
            }
        }

        FileStream? ticket = null;
        string? ticketPath = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (access == TemplateStoreAccess.Read && HasPendingWriter(directory))
                {
                    ReportWait(TemplateStoreWaitReason.LifecycleOperation);
                    await Task.Delay(25, cancellationToken);
                    continue;
                }

                using (FileStream? gate = TryOpen(Path.Combine(directory, "gate.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    if (gate is not null)
                    {
                        string[] tickets = LiveTickets(directory);
                        if (access == TemplateStoreAccess.Write && ticket is null)
                        {
                            long next = tickets.Length == 0 ? 1 : checked(long.Parse(Path.GetFileName(tickets[^1])[..20], CultureInfo.InvariantCulture) + 1);
                            ticketPath = Path.Combine(directory, $"{next:D20}-{Guid.NewGuid():N}.writer");
                            ticket = new(ticketPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                                bufferSize: 1, FileOptions.DeleteOnClose);
                            tickets = [.. tickets, ticketPath];
                        }

                        bool admitted = access == TemplateStoreAccess.Read ? tickets.Length == 0 : tickets[0] == ticketPath;
                        if (admitted)
                        {
                            FileStream? owner = TryOpen(owners, FileMode.Open, FileAccess.Read,
                                access == TemplateStoreAccess.Read ? FileShare.Read : FileShare.None);
                            if (owner is not null)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    owner.Dispose();
                                    cancellationToken.ThrowIfCancellationRequested();
                                }

                                if (ticket is null)
                                {
                                    return owner;
                                }

                                WriterOwnership ownership = new(owner, ticket);
                                ticket = null;
                                ticketPath = null;
                                return ownership;
                            }
                        }

                        ReportWait(access == TemplateStoreAccess.Write && admitted
                            ? TemplateStoreWaitReason.ActiveReaders : TemplateStoreWaitReason.LifecycleOperation);
                    }
                }

                await Task.Delay(25, cancellationToken);
            }
        }
        finally
        {
            ticket?.Dispose();
        }
    }

    private async Task<FileStream> AcquireGateAsync(string directory, TemplateStoreAccess access, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason> waiting)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? gate = access == TemplateStoreAccess.Read && HasPendingWriter(directory) ? null
                : TryOpen(Path.Combine(directory, "gate.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (gate is not null)
            {
                return gate;
            }

            waiting(TemplateStoreWaitReason.LifecycleOperation);
            await Task.Delay(25, cancellationToken);
        }
    }

    private string[] LiveTickets(string directory)
    {
        List<string> live = [];
        foreach (string path in Directory.GetFiles(directory, "*.writer").Order(StringComparer.Ordinal))
        {
            using FileStream? abandoned = TryOwnMarker(path);
            if (abandoned is null)
            {
                live.Add(path);
            }
        }

        return [.. live];
    }

    private bool HasPendingWriter(string directory)
    {
        foreach (string path in Directory.GetFiles(directory, "*.intent"))
        {
            using FileStream? abandoned = TryOwnMarker(path);
            if (abandoned is null)
            {
                return true;
            }
        }

        return false;
    }

    private FileStream? TryOpen(string path, FileMode mode, FileAccess access, FileShare share, FileOptions options = FileOptions.None)
    {
        try
        {
            return new(path, mode, access, share, bufferSize: 1, options);
        }
        catch (IOException exception) when (IsContention(exception))
        {
            return null;
        }
    }

    // Revalidate Unix pathname/inode identity before deletion, including inspection after another owner unlinks.
    private FileStream? TryOwnMarker(string path)
        => TryOpen(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, FileOptions.DeleteOnClose);

    private static bool IsContention(IOException exception)
        => OperatingSystem.IsWindows() ? exception.HResult == unchecked((int)0x80070020)
            : (OperatingSystem.IsLinux() && exception.HResult == 11) || (OperatingSystem.IsMacOS() && exception.HResult == 35);

    private sealed class WriterOwnership(FileStream owner, FileStream ticket) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                ticket.Dispose();
            }
            finally
            {
                owner.Dispose();
            }
        }
    }

    private async Task<FileStream> AcquireWriterIntentAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.intent");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // OpenOrCreate lets Unix revalidate the pathname after locking; None is required for an exclusive flock.
            FileStream? intent = TryOwnMarker(path);
            if (intent is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    intent.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return intent;
            }

            await Task.Delay(25, cancellationToken);
        }
    }
}
