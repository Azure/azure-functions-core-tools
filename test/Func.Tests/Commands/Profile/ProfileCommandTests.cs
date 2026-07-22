// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Profile;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

        profileCommand.Should().NotBeNull();
        profileCommand!.Subcommands.Should().Contain(c => c.Name == "list");
        profileCommand.Subcommands.Should().Contain(c => c.Name == "show");
        profileCommand.Subcommands.Should().Contain(c => c.Name == "set");
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

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain("  Project profiles    my-preview (default), flex");
        _interaction.Lines.Should().Contain("TABLE: [Name, Source, Host Version, Extension Bundle, Status]");
        _interaction.Lines.Should().Contain("  ROW: [flex, built-in, [4.0.0, 5.0.0), [3.0.0, 5.0.0), stable]");
        _interaction.Lines.Should().Contain("  ROW: [my-preview, user, [4.1.0, 5.0.0), -, stable]");
        _interaction.Lines.Should().Contain("  ROW: [staging, project, [4.0.0, 5.0.0), [3.0.0, 5.0.0), stable]");
    }

    [Fact]
    public async Task List_SourceFilter_UsesFilteredSourceWhenProfileNamesOverlap()
    {
        ProfileListCommand command = CreateListCommand(
            ProjectOptions(),
            Source(ProfileSourceKind.Project, Profile("flex", hostRange: "[9.0.0, 10.0.0)")),
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "--source", "built-in", _tempDir);

        exit.Should().Be(0);
        string row = _interaction.Lines.Should().ContainSingle(l => l.StartsWith("  ROW:")).Which;
        row.Should().Be("  ROW: [flex, built-in, [4.0.0, 5.0.0), -, stable]");
    }

    [Fact]
    public async Task List_Json_WritesStructuredProfiles()
    {
        ProfileListCommand command = CreateListCommand(
            ProjectOptions(["flex"], defaultProfile: "flex"),
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "--json", _tempDir);

        exit.Should().Be(0);
        _interaction.Lines.Should().NotContain(l => l.StartsWith("TABLE:"));
        string json = _interaction.Lines.Single(l => l.StartsWith("JSON:"));
        json.Should().Contain("\"defaultProfile\":\"flex\"");
        json.Should().Contain("\"name\":\"flex\"");
        json.Should().Contain("\"source\":\"built-in\"");
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

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain("  Name    staging");
        _interaction.Lines.Should().Contain("  Source    project");
        _interaction.Lines.Should().Contain("  Host version    [4.0.0, 5.0.0)");
        _interaction.Lines.Should().Contain("  Extension bundle    [3.0.0, 5.0.0)");
        _interaction.Lines.Should().Contain("  Workers    node [4.0.0]");
        _interaction.Lines.Should().Contain("  Supported runtimes    node");
    }

    [Fact]
    public async Task Show_Raw_WritesDefinitionWithoutInheritance()
    {
        ProfileShowCommand command = CreateShowCommand(
            Source(ProfileSourceKind.Project, Profile("staging", extends: "flex", hostRange: null)),
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "staging", "--raw", _tempDir);

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain("  Name    staging");
        _interaction.Lines.Should().Contain("  Extends    flex");
        _interaction.Lines.Should().Contain("  Host version    -");
    }

    [Fact]
    public async Task Show_MissingProfile_ThrowsGracefulException()
    {
        ProfileShowCommand command = CreateShowCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(command, "missing", _tempDir)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("Profile 'missing' was not found.");
    }

    [Fact]
    public async Task Set_AddsProfileToExistingProjectConfigAndSetsDefault()
    {
        WriteProjectConfig("{}");
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex", hostRange: "[4.0.0, 5.0.0)")));

        int exit = await InvokeAsync(command, "flex", _tempDir);

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain("SUCCESS: Profile 'flex' set as this project's default.");
        _interaction.Lines.Should().Contain("HINT: Added 'flex' to this project's profiles list.");
        using JsonDocument document = ReadProjectConfig();
        JsonElement root = document.RootElement;
        root.GetProperty("defaultProfile").GetString().Should().Be("flex");
        root.GetProperty("profiles").EnumerateArray().Should().ContainSingle().Which.GetString().Should().Be("flex");
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

        exit.Should().Be(0);
        using JsonDocument document = ReadProjectConfig();
        JsonElement root = document.RootElement;
        root.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        root.GetProperty("defaultProfile").GetString().Should().Be("staging");
        string?[] profiles = [.. root.GetProperty("profiles").EnumerateArray().Select(p => p.GetString())];
        string?[] expectedProfiles = ["flex", "staging"];
        profiles.Should().Equal(expectedProfiles);
    }

    [Fact]
    public async Task Set_ExistingProfile_DoesNotDuplicateProfile()
    {
        WriteProjectConfig("""{"profiles":["FLEX"],"defaultProfile":"FLEX"}""");
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        int exit = await InvokeAsync(command, "flex", _tempDir);

        exit.Should().Be(0);
        _interaction.Lines.Should().NotContain(l => l.StartsWith("HINT: Added", StringComparison.Ordinal));
        using JsonDocument document = ReadProjectConfig();
        JsonElement root = document.RootElement;
        root.GetProperty("profiles").EnumerateArray().Should().ContainSingle().Which.GetString().Should().Be("FLEX");
        root.GetProperty("defaultProfile").GetString().Should().Be("FLEX");
    }

    [Fact]
    public async Task Set_MissingProfile_ThrowsGracefulException()
    {
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(command, "missing", _tempDir)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("Profile 'missing' was not found.");
        _fileSystem.Exists(ProjectConfigPath()).Should().BeFalse();
    }

    [Fact]
    public async Task Set_MissingProjectConfig_ThrowsGracefulException()
    {
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(command, "flex", _tempDir)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("Project not initialized.");
        ex.Message.Should().Contain("Run 'func init'");
        _fileSystem.Exists(ProjectConfigPath()).Should().BeFalse();
    }

    [Fact]
    public async Task Set_InvalidProjectConfig_ThrowsGracefulException()
    {
        WriteProjectConfig("{ not valid json");
        ProfileSetCommand command = CreateSetCommand(
            Source(ProfileSourceKind.BuiltIn, Profile("flex")));

        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(command, "flex", _tempDir)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("contains invalid JSON");
    }

    private ProfileListCommand CreateListCommand(ProjectProfileOptions options, params IProfileSource[] sources)
    {
        var catalog = new ProfileCatalog(sources, NullLogger<ProfileCatalog>.Instance);
        var optionsMonitor = new TestOptionsMonitor<ProjectProfileOptions>(options);
        return new ProfileListCommand(_interaction, catalog, optionsMonitor);
    }

    private ProfileShowCommand CreateShowCommand(params IProfileSource[] sources)
    {
        var catalog = new ProfileCatalog(sources, NullLogger<ProfileCatalog>.Instance);
        return new ProfileShowCommand(_interaction, catalog);
    }

    private ProfileSetCommand CreateSetCommand(params IProfileSource[] sources)
    {
        var catalog = new ProfileCatalog(sources, NullLogger<ProfileCatalog>.Instance);
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

        public Task DeleteIfExistsAsync(string path)
        {
            _files.Remove(path);
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
