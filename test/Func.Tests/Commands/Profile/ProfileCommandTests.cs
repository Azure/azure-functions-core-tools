// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Profile;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Profile;

public sealed class ProfileCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeProfileFileSystem _fileSystem = new();
    private readonly TestInteractionService _interaction = new();

    public ProfileCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-profile-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProfileCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        Command? profileCommand = root.Subcommands.SingleOrDefault(c => c.Name == "profile");

        Assert.NotNull(profileCommand);
        Assert.Contains(profileCommand!.Subcommands, c => c.Name == "list");
        Assert.Contains(profileCommand.Subcommands, c => c.Name == "show");
        Assert.Contains(profileCommand.Subcommands, c => c.Name == "set");
    }

    [Fact]
    public async Task List_WritesProfilesWithProjectSelection()
    {
        ProfileListCommand command = CreateListCommand(
            ProjectOptions(["my-preview", "flex"], defaultProfile: "my-preview"),
            Source(ProfileSourceKind.Project, Profile("staging", extends: "flex", hostRange: null)),
            Source(ProfileSourceKind.User, Profile("my-preview", hostRange: "[4.1.0, 5.0.0)")),
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)", bundleRange: "[3.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, _tempDir);

        Assert.Equal(0, exit);
        Assert.Contains("  Project profiles    my-preview (default), flex", _interaction.Lines);
        Assert.Contains("TABLE: [Name, Source, Host Version, Extension Bundle, Status]", _interaction.Lines);
        Assert.Contains("  ROW: [flex, built-in, [4.0.0, 5.0.0), [3.0.0, 5.0.0), stable]", _interaction.Lines);
        Assert.Contains("  ROW: [my-preview, user, [4.1.0, 5.0.0), -, stable]", _interaction.Lines);
        Assert.Contains("  ROW: [staging, project, [4.0.0, 5.0.0), [3.0.0, 5.0.0), stable]", _interaction.Lines);
    }

    [Fact]
    public async Task List_SourceFilter_UsesFilteredSourceWhenProfileNamesOverlap()
    {
        ProfileListCommand command = CreateListCommand(
            ProjectOptions(),
            Source(ProfileSourceKind.Project, Profile("flex", hostRange: "[9.0.0, 10.0.0)")),
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "--source", "built-in", _tempDir);

        Assert.Equal(0, exit);
        string row = Assert.Single(_interaction.Lines, l => l.StartsWith("  ROW:"));
        Assert.Equal("  ROW: [flex, built-in, [4.0.0, 5.0.0), -, stable]", row);
    }

    [Fact]
    public async Task List_Json_WritesStructuredProfiles()
    {
        ProfileListCommand command = CreateListCommand(
            ProjectOptions(["flex"], defaultProfile: "flex"),
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "--json", _tempDir);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
        string json = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        Assert.Contains("\"defaultProfile\":\"flex\"", json);
        Assert.Contains("\"name\":\"flex\"", json);
        Assert.Contains("\"source\":\"built-in\"", json);
    }

    [Fact]
    public async Task Show_WritesResolvedProfile()
    {
        ProfileShowCommand command = CreateShowCommand(
            Source(ProfileSourceKind.Project, Profile(
                "staging",
                extends: "flex",
                hostRange: null,
                workers: Workers(("node", "[4.0.0]")))),
            Source(ProfileSourceKind.BuiltIn, Profile(
                "flex",
                hostRange: "[4.0.0, 5.0.0)",
                bundleRange: "[3.0.0, 5.0.0)",
                supportedRuntimes: ["node"])));

        int exit = await InvokeAsync(command, "staging", _tempDir);

        Assert.Equal(0, exit);
        Assert.Contains("  Name    staging", _interaction.Lines);
        Assert.Contains("  Source    project", _interaction.Lines);
        Assert.Contains("  Host version    [4.0.0, 5.0.0)", _interaction.Lines);
        Assert.Contains("  Extension bundle    [3.0.0, 5.0.0)", _interaction.Lines);
        Assert.Contains("  Workers    node [4.0.0]", _interaction.Lines);
        Assert.Contains("  Supported runtimes    node", _interaction.Lines);
    }

    [Fact]
    public async Task Show_Raw_WritesDefinitionWithoutInheritance()
    {
        ProfileShowCommand command = CreateShowCommand(
            Source(ProfileSourceKind.Project, Profile("staging", extends: "flex", hostRange: null)),
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "staging", "--raw", _tempDir);

        Assert.Equal(0, exit);
        Assert.Contains("  Name    staging", _interaction.Lines);
        Assert.Contains("  Extends    flex", _interaction.Lines);
        Assert.Contains("  Host version    -", _interaction.Lines);
    }

    [Fact]
    public async Task Show_MissingProfile_ThrowsGracefulException()
    {
        ProfileShowCommand command = CreateShowCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(command, "missing", _tempDir));

        Assert.Contains("Profile 'missing' was not found.", ex.Message);
    }

    [Fact]
    public async Task Set_AddsProfileToExistingProjectConfigAndSetsDefault()
    {
        WriteProjectConfig("{}");
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "flex", _tempDir);

        Assert.Equal(0, exit);
        Assert.Contains("SUCCESS: Profile 'flex' set as this project's default.", _interaction.Lines);
        Assert.Contains("HINT: Added 'flex' to this project's profiles list.", _interaction.Lines);
        using JsonDocument document = ReadProjectConfig();
        JsonElement root = document.RootElement;
        Assert.Equal("flex", root.GetProperty("defaultProfile").GetString());
        JsonElement profile = Assert.Single(root.GetProperty("profiles").EnumerateArray());
        Assert.Equal("flex", profile.GetString());
    }

    [Fact]
    public async Task Set_PreservesExistingProjectConfigAndAddsProfile()
    {
        WriteProjectConfig(
            """
            {
              "stack": {
                "runtime": "node"
              },
              "profiles": [ "flex" ],
              "defaultProfile": "flex"
            }
            """);
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex"), Profile("staging")));

        int exit = await InvokeAsync(command, "staging", _tempDir);

        Assert.Equal(0, exit);
        using JsonDocument document = ReadProjectConfig();
        JsonElement root = document.RootElement;
        Assert.Equal("node", root.GetProperty("stack").GetProperty("runtime").GetString());
        Assert.Equal("staging", root.GetProperty("defaultProfile").GetString());
        string?[] profiles = [.. root.GetProperty("profiles").EnumerateArray().Select(p => p.GetString())];
        string?[] expectedProfiles = ["flex", "staging"];
        Assert.Equal(expectedProfiles, profiles);
    }

    [Fact]
    public async Task Set_ExistingProfile_DoesNotDuplicateProfile()
    {
        WriteProjectConfig("""{"profiles":["FLEX"],"defaultProfile":"FLEX"}""");
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        int exit = await InvokeAsync(command, "flex", _tempDir);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("HINT: Added", StringComparison.Ordinal));
        using JsonDocument document = ReadProjectConfig();
        JsonElement root = document.RootElement;
        JsonElement profile = Assert.Single(root.GetProperty("profiles").EnumerateArray());
        Assert.Equal("FLEX", profile.GetString());
        Assert.Equal("FLEX", root.GetProperty("defaultProfile").GetString());
    }

    [Fact]
    public async Task Set_MissingProfile_ThrowsGracefulException()
    {
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(command, "missing", _tempDir));

        Assert.Contains("Profile 'missing' was not found.", ex.Message);
        Assert.False(_fileSystem.Exists(ProjectConfigPath()));
    }

    [Fact]
    public async Task Set_MissingProjectConfig_ThrowsGracefulException()
    {
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(command, "flex", _tempDir));

        Assert.Contains("Project not initialized.", ex.Message);
        Assert.Contains("Run 'func init'", ex.Message);
        Assert.False(_fileSystem.Exists(ProjectConfigPath()));
    }

    [Fact]
    public async Task Set_InvalidProjectConfig_ThrowsGracefulException()
    {
        WriteProjectConfig("{ not valid json");
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(command, "flex", _tempDir));

        Assert.Contains("contains invalid JSON", ex.Message);
    }

    private ProfileListCommand CreateListCommand(ProjectProfileOptions options, params IProfileSource[] sources)
    {
        var catalog = new ProfileCatalog(sources);
        var optionsMonitor = new TestOptionsMonitor<ProjectProfileOptions>(options);
        return new ProfileListCommand(_interaction, catalog, optionsMonitor);
    }

    private ProfileShowCommand CreateShowCommand(params IProfileSource[] sources)
    {
        var catalog = new ProfileCatalog(sources);
        return new ProfileShowCommand(_interaction, catalog);
    }

    private ProfileSetCommand CreateSetCommand(params IProfileSource[] sources)
    {
        var catalog = new ProfileCatalog(sources);
        var store = new ProjectProfileConfigStore(_fileSystem);
        return new ProfileSetCommand(_interaction, catalog, store);
    }

    private static Task<int> InvokeAsync(FuncCliCommand command, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(command);
        ParseResult result = root.Parse(new[] { command.Name }.Concat(args).ToArray());
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return result.InvokeAsync(config);
    }

    private static ProjectProfileOptions ProjectOptions(IReadOnlyList<string>? profiles = null, string? defaultProfile = null)
        => new()
        {
            Profiles = profiles?.ToList() ?? [],
            DefaultProfile = defaultProfile,
        };

    private static IProfileSource Source(ProfileSourceKind kind, params KeyValuePair<string, ProfileDefinition>[] profiles)
        => new FakeProfileSource(new ProfileSourceInfo(kind, $"{kind} profiles"), profiles);

    private static KeyValuePair<string, ProfileDefinition> Profile(
        string name,
        string? hostRange = "[1.0.0, 2.0.0)",
        string? bundleRange = null,
        string? status = null,
        string? deprecationUrl = null,
        string? extends = null,
        Dictionary<string, ProfileWorkerConstraint?>? workers = null,
        List<string>? supportedRuntimes = null)
        => new(name, new ProfileDefinition
        {
            Status = status,
            DeprecationUrl = deprecationUrl,
            Extends = extends,
            Host = hostRange is null ? null : new ProfileVersionConstraint { Version = hostRange },
            ExtensionBundle = bundleRange is null ? null : new ProfileVersionConstraint { Version = bundleRange },
            Workers = workers,
            SupportedRuntimes = supportedRuntimes,
        });

    private static Dictionary<string, ProfileWorkerConstraint?> Workers(params (string Runtime, string Range)[] workers)
        => workers.ToDictionary(
            worker => worker.Runtime,
            worker => (ProfileWorkerConstraint?)new ProfileWorkerConstraint { Version = worker.Range },
            StringComparer.OrdinalIgnoreCase);

    private void WriteProjectConfig(string contents)
        => _fileSystem.WriteAllText(ProjectConfigPath(), contents);

    private JsonDocument ReadProjectConfig()
        => JsonDocument.Parse(_fileSystem.ReadAllText(ProjectConfigPath()));

    private string ProjectConfigPath()
        => Path.Combine(_tempDir, ".func", "config.json");

    private sealed class FakeProfileFileSystem : IProfileFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files.TryGetValue(path, out string? contents);
            return Task.FromResult(contents);
        }

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteAllText(path, contents);
            return Task.CompletedTask;
        }

        public Task WriteAllTextAtomicAsync(string path, string contents, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteAllText(path, contents);
            return Task.CompletedTask;
        }

        public Task EnsureDirectoryExistsAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void WriteAllText(string path, string contents)
            => _files[path] = contents;

        public string ReadAllText(string path)
            => _files[path];

        public bool Exists(string path)
            => _files.ContainsKey(path);
    }

    private sealed class FakeProfileSource(
        ProfileSourceInfo source,
        IReadOnlyList<KeyValuePair<string, ProfileDefinition>> profiles) : IProfileSource
    {
        private readonly ProfileSourceInfo _source = source;
        private readonly IReadOnlyList<KeyValuePair<string, ProfileDefinition>> _profiles = profiles;

        public Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, ProfileDefinition> profiles = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, ProfileDefinition definition) in _profiles)
            {
                profiles.Add(name, definition);
            }

            var snapshot = new ProfileSourceSnapshot(_source, profiles);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        private readonly TOptions _value = value;

        public TOptions CurrentValue => _value;

        public TOptions Get(string? name) => _value;

        public IDisposable OnChange(Action<TOptions, string?> listener) => NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
