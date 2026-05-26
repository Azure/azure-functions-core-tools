// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
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
