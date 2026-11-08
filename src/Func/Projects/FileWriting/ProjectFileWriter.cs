// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Projects.FileWriting;

/// <summary>
/// Writes new project files without replacing existing content. Input bytes describe planned output, not completed changes.
/// </summary>
internal sealed class ProjectFileWriter(WindowsProjectFileSystem fileSystem)
{
    internal const int MaximumPlanEntries = 1024;
    internal const int MaximumPathDepth = 32;
    private readonly WindowsProjectFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ProjectWriteResult> WriteAsync(
        string destination,
        IReadOnlyDictionary<string, ImmutableArray<byte>> files,
        IReadOnlyList<string> directories,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(directories);
        if ((long)files.Count + directories.Count > MaximumPlanEntries)
        {
            throw new ArgumentException($"Planned output exceeds the {MaximumPlanEntries}-entry limit.");
        }

        Dictionary<string, ImmutableArray<byte>> plannedFiles = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> plannedDirectories = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> explicitDirectories = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            string path = NormalizePath(directory);
            if (!explicitDirectories.Add(path))
            {
                throw new ArgumentException($"Planned directory '{path}' is duplicated.", nameof(directories));
            }

            AddDirectoryAndParents(path);
        }

        foreach (KeyValuePair<string, ImmutableArray<byte>> file in files)
        {
            string path = NormalizePath(file.Key);
            if (file.Value.IsDefault || !plannedFiles.TryAdd(path, file.Value))
            {
                throw new ArgumentException($"Planned file '{path}' is duplicated or has uninitialized content.", nameof(files));
            }

            int separator = path.LastIndexOf('\\');
            if (separator >= 0)
            {
                AddDirectoryAndParents(path[..separator]);
            }

            CheckEntryLimit();
        }

        if (plannedFiles.Keys.Any(plannedDirectories.Contains))
        {
            throw new ArgumentException("A planned path is both a file and a directory.");
        }

        List<ProjectWriteEffect> effects = [new(destination, true, ProjectWriteStatus.NotAttempted)];
        effects.AddRange(plannedDirectories.OrderBy(path => path.Count(character => character == '\\'))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProjectWriteEffect(path, true, ProjectWriteStatus.NotAttempted)));
        effects.AddRange(plannedFiles.Keys.Select(path => new ProjectWriteEffect(path, false, ProjectWriteStatus.NotAttempted)));
        OperationCanceledException? cancellation = null;

        WindowsProjectWriteSession session;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session = _fileSystem.OpenSession(destination, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (exception is OperationCanceledException canceled)
            {
                cancellation = canceled;
            }
            else
            {
                effects[0] = effects[0] with { Status = ProjectWriteStatus.Failed, Error = exception };
            }

            return Finish();
        }

        using (session)
        {
            bool conflicts = false;
            for (int index = 0; index < effects.Count; index++)
            {
                ProjectWriteEffect effect = effects[index];
                string path = index == 0 ? string.Empty : effect.Path;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (effect.IsDirectory)
                    {
                        if (session.DirectoryExists(path))
                        {
                            effects[index] = effect with { Status = ProjectWriteStatus.Preserved };
                        }
                    }
                    else
                    {
                        session.CheckFileAbsent(path);
                    }
                }
                catch (Exception exception) when (IsExpectedFailure(exception))
                {
                    conflicts = true;
                    if (exception is OperationCanceledException canceled)
                    {
                        cancellation = canceled;
                        break;
                    }

                    effects[index] = effect with { Status = ProjectWriteStatus.Failed, Error = exception };
                }
            }

            if (conflicts)
            {
                return Finish();
            }

            for (int index = 0; index < effects.Count; index++)
            {
                ProjectWriteEffect effect = effects[index];
                if (effect.Status == ProjectWriteStatus.Preserved)
                {
                    continue;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (effect.IsDirectory)
                    {
                        effects[index] = session.CreateDirectory(index == 0 ? string.Empty : effect.Path, cancellationToken) with { Path = effect.Path };
                    }
                    else
                    {
                        effects[index] = await session.CreateFileAsync(effect.Path, plannedFiles[effect.Path], cancellationToken);
                    }

                    if (effects[index].Status != ProjectWriteStatus.Created)
                    {
                        break;
                    }
                }
                catch (Exception exception) when (exception.Data[typeof(ProjectWriteEffect)] is ProjectWriteEffect partialEffect)
                {
                    partialEffect = partialEffect with { Path = effect.Path };
                    effects[index] = partialEffect;
                    exception.Data[typeof(ProjectWriteEffect)] = partialEffect;
                    exception.Data[typeof(ProjectWriteResult)] = Finish();
                    throw;
                }
                catch (Exception exception) when (IsExpectedFailure(exception))
                {
                    if (exception is OperationCanceledException canceled)
                    {
                        cancellation = canceled;
                    }
                    else
                    {
                        effects[index] = effect with { Status = ProjectWriteStatus.Failed, Error = exception };
                    }

                    break;
                }
            }
        }

        return Finish();

        ProjectWriteResult Finish()
        {
            cancellation ??= effects.Select(effect => effect.Error).OfType<OperationCanceledException>().FirstOrDefault();
            if (cancellation is null && cancellationToken.IsCancellationRequested)
            {
                cancellation = new OperationCanceledException(cancellationToken);
            }

            return new([.. effects], cancellation);
        }

        void CheckEntryLimit()
        {
            if ((long)plannedFiles.Count + plannedDirectories.Count > MaximumPlanEntries)
            {
                throw new ArgumentException($"Planned output, including parent directories, exceeds the {MaximumPlanEntries}-entry limit.");
            }
        }

        void AddDirectoryAndParents(string path)
        {
            plannedDirectories.Add(path);
            CheckEntryLimit();
            int separator = path.LastIndexOf('\\');
            while (separator >= 0)
            {
                plannedDirectories.Add(path[..separator]);
                CheckEntryLimit();
                separator = path.LastIndexOf('\\', separator - 1);
            }
        }
    }

    internal static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or OperationCanceledException or NotSupportedException;

    internal static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('/', '\\');
        string[] components = normalized.Split('\\');
        if (components.Length > MaximumPathDepth)
        {
            throw new ArgumentException($"Path '{path}' exceeds the {MaximumPathDepth}-component limit.", nameof(path));
        }

        foreach (string segment in components)
        {
            if (segment.Length is 0 or > 255 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Path '{path}' contains an empty, traversal, ambiguous, or protected Git component.", nameof(path));
            }

            if (segment.Any(character => char.IsControl(character) || "<>:\"|?*~".Contains(character)))
            {
                throw new ArgumentException($"Path '{path}' contains an unsupported character or short-name alias.", nameof(path));
            }

            string stem = segment.Split('.')[0].TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4
                    && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                    && "123456789\u00b9\u00b2\u00b3".Contains(stem[3])))
            {
                throw new ArgumentException($"Path '{path}' contains a reserved device name.", nameof(path));
            }
        }

        return normalized;
    }
}
