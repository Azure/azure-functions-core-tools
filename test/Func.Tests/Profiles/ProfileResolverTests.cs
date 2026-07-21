// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Tests.Profiles;

public class ProfileResolverTests
{
    private readonly DirectoryInfo _workingDirectory = new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    private readonly TestInteractionService _interaction = new();
    private ProjectProfileOptions _projectProfileOptions = new();
    private UserProfilePreferenceOptions _userProfilePreferenceOptions = new();

    [Fact]
    public async Task ResolveAsync_ReturnsNoneWithoutDiagnostics_WhenNoProfileConfiguredOrRequested()
    {
        var source = new FakeProfileSource(Source(ProfileSourceKind.BuiltIn), []);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution resolution = await resolver.ResolveAsync(Context(), CancellationToken.None);

        Assert.IsType<ProfileResolution.None>(resolution);
        Assert.Empty(resolution.Diagnostics);
        Assert.Equal(0, source.LoadCount);
    }

    [Fact]
    public async Task ResolveAsync_UsesRequestedProfileAndWarns_WhenNotDeclaredByProject()
    {
        SetProjectConfig("""
            {
              "profiles": [ "windows-consumption" ]
            }
            """);
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver, requestedProfile: "flex");

        Assert.Equal("flex", resolution.Profile.Name);
        ProfileDiagnostic diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ProfileDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("not declared", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_UsesDefaultProfileFromProjectConfig()
    {
        SetProjectConfig("""
            {
              "profiles": [ "flex", "linux-premium" ],
              "defaultProfile": "linux-premium"
            }
            """);
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
            Profile("linux-premium", hostRange: "[2.0.0, 3.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver);

        Assert.Equal("linux-premium", resolution.Profile.Name);
        Assert.True(resolution.Profile.HostVersionRange.Satisfies(NuGetVersion.Parse("2.5.0")));
    }

    [Fact]
    public async Task ResolveAsync_UsesProjectDefaultProfileBeforeUserDefaultProfile()
    {
        SetProjectConfig("""
            {
              "profiles": [ "flex", "linux-premium" ],
              "defaultProfile": "linux-premium"
            }
            """);
        SetUserDefaultProfile("flex");
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
            Profile("linux-premium", hostRange: "[2.0.0, 3.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver);

        Assert.Equal("linux-premium", resolution.Profile.Name);
    }

    [Fact]
    public async Task ResolveAsync_UsesUserDefaultProfile_WhenProjectHasNoDefaultProfile()
    {
        SetUserDefaultProfile("flex");
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver);

        Assert.Equal("flex", resolution.Profile.Name);
    }

    [Fact]
    public async Task ResolveAsync_UsesUserDefaultProfile_WhenDeclaredByProject()
    {
        SetProjectConfig("""
            {
              "profiles": [ "flex", "linux-premium" ]
            }
            """);
        SetUserDefaultProfile("linux-premium");
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
            Profile("linux-premium", hostRange: "[2.0.0, 3.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver, canPrompt: true);

        Assert.Equal("linux-premium", resolution.Profile.Name);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresUserDefaultProfile_WhenNotDeclaredByProject()
    {
        SetProjectConfig("""
            {
              "profiles": [ "flex" ]
            }
            """);
        SetUserDefaultProfile("linux-premium");
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
            Profile("linux-premium", hostRange: "[2.0.0, 3.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver);

        Assert.Equal("flex", resolution.Profile.Name);
        ProfileDiagnostic diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ProfileDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("will be ignored", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_PromptsForProfile_WhenProjectHasMultipleProfilesAndNoDefault()
    {
        SetProjectConfig("""
            {
              "profiles": [ "flex", "linux-premium" ]
            }
            """);
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
            Profile("linux-premium", hostRange: "[2.0.0, 3.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver, canPrompt: true);

        Assert.Equal("flex", resolution.Profile.Name);
        Assert.Contains(_interaction.Lines, line => line.StartsWith("SELECT: Select an Azure Functions profile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenProjectHasMultipleProfilesAndCannotPrompt()
    {
        SetProjectConfig("""
            {
              "profiles": [ "flex", "linux-premium" ]
            }
            """);
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("flex", hostRange: "[1.0.0, 2.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileConfigurationException ex = await Assert.ThrowsAsync<ProfileConfigurationException>(
            () => resolver.ResolveAsync(Context(canPrompt: false), CancellationToken.None));

        Assert.Contains("--profile", ex.Message);
        Assert.Contains("defaultProfile", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_UsesFirstSourceThatDefinesProfile()
    {
        FakeProfileSource projectSource = new(Source(ProfileSourceKind.Project), [
            Profile("shared", hostRange: "[3.0.0, 4.0.0)"),
        ]);
        FakeProfileSource userSource = new(Source(ProfileSourceKind.User), [
            Profile("shared", hostRange: "[2.0.0, 3.0.0)"),
        ]);
        FakeProfileSource builtInSource = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("shared", hostRange: "[1.0.0, 2.0.0)"),
        ]);
        ProfileResolver resolver = CreateResolver(projectSource, userSource, builtInSource);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver, requestedProfile: "shared");

        Assert.Equal(ProfileSourceKind.Project, resolution.Profile.Source.Kind);
        Assert.True(resolution.Profile.HostVersionRange.Satisfies(NuGetVersion.Parse("3.5.0")));
        Assert.False(resolution.Profile.HostVersionRange.Satisfies(NuGetVersion.Parse("2.5.0")));
    }

    [Fact]
    public async Task ResolveAsync_MergesInheritanceAndRemovesNullOverrides()
    {
        ProfileDefinition parent = Definition(
            hostRange: "[1.0.0, 3.0.0)",
            bundleRange: "[4.0.0, 5.0.0)",
            workers: new Dictionary<string, ProfileWorkerConstraint?>
            {
                ["node"] = new() { Version = "[3.13.0]" },
                ["python"] = new() { Version = "[4.43.0]" },
            },
            supportedRuntimes: ["node", "python"]);
        ProfileDefinition child = Definition(
            hostRange: null,
            extends: "base",
            bundleRange: "[5.0.0, 6.0.0)",
            workers: new Dictionary<string, ProfileWorkerConstraint?>
            {
                ["python"] = null,
                ["go"] = new() { Version = "[1.0.0]" },
            });
        FakeProfileSource source = new(Source(ProfileSourceKind.Project), [Entry("base", parent), Entry("child", child)]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver, requestedProfile: "child");

        Assert.True(resolution.Profile.HostVersionRange.Satisfies(NuGetVersion.Parse("2.0.0")));
        Assert.True(resolution.Profile.ExtensionBundleVersionRange!.Satisfies(NuGetVersion.Parse("5.5.0")));
        Assert.Equal(["node", "python"], resolution.Profile.SupportedRuntimes);
        Assert.True(resolution.Profile.WorkerVersionRanges["node"].Satisfies(NuGetVersion.Parse("3.13.0")));
        Assert.True(resolution.Profile.WorkerVersionRanges["go"].Satisfies(NuGetVersion.Parse("1.0.0")));
        Assert.False(resolution.Profile.WorkerVersionRanges.ContainsKey("python"));
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenInheritanceCycles()
    {
        FakeProfileSource source = new(Source(ProfileSourceKind.Project), [
            Profile("a", extends: "b"),
            Profile("b", extends: "a"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileConfigurationException ex = await Assert.ThrowsAsync<ProfileConfigurationException>(
            () => resolver.ResolveAsync(Context(requestedProfile: "a"), CancellationToken.None));

        Assert.Contains("Circular profile inheritance", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_AddsWarningDiagnostic_ForDeprecatedProfile()
    {
        FakeProfileSource source = new(Source(ProfileSourceKind.BuiltIn), [
            Profile("linux-consumption", status: "deprecated", deprecationUrl: "https://example.test/deprecation"),
        ]);
        ProfileResolver resolver = CreateResolver(source);

        ProfileResolution.Resolved resolution = await ResolveProfileAsync(resolver, requestedProfile: "linux-consumption");

        ProfileDiagnostic diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ProfileDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("deprecated", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.test/deprecation", diagnostic.Message);
    }

    private ProfileResolver CreateResolver(params IProfileSource[] sources)
    {
        var catalog = new ProfileCatalog(sources, NullLogger<ProfileCatalog>.Instance);
        var projectOptionsMonitor = new TestOptionsMonitor<ProjectProfileOptions>(_projectProfileOptions);
        var userOptionsMonitor = new TestOptionsMonitor<UserProfilePreferenceOptions>(_userProfilePreferenceOptions);
        return new ProfileResolver(catalog, projectOptionsMonitor, userOptionsMonitor, _interaction);
    }

    private async Task<ProfileResolution.Resolved> ResolveProfileAsync(
        ProfileResolver resolver,
        string? requestedProfile = null,
        bool canPrompt = false)
    {
        ProfileResolution resolution = await resolver.ResolveAsync(
            Context(requestedProfile, canPrompt),
            CancellationToken.None);

        return Assert.IsType<ProfileResolution.Resolved>(resolution);
    }

    private ProfileResolutionContext Context(string? requestedProfile = null, bool canPrompt = false)
        => new(_workingDirectory, requestedProfile, canPrompt);

    private void SetProjectConfig(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes);
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
        var setup = new ProjectProfileOptionsSetup(configuration);
        ProjectProfileOptions options = new();
        setup.Configure(options);
        _projectProfileOptions = options;
    }

    private void SetUserDefaultProfile(string profile)
        => _userProfilePreferenceOptions = new UserProfilePreferenceOptions { DefaultProfile = profile };

    private static ProfileSourceInfo Source(ProfileSourceKind kind)
        => new(kind, $"{kind} profiles");

    private static KeyValuePair<string, ProfileDefinition> Profile(
        string name,
        string? hostRange = "[1.0.0, 2.0.0)",
        string? bundleRange = null,
        string? status = null,
        string? deprecationUrl = null,
        string? extends = null,
        Dictionary<string, ProfileWorkerConstraint?>? workers = null,
        List<string>? supportedRuntimes = null)
        => Entry(name, Definition(
            hostRange,
            bundleRange,
            status,
            deprecationUrl,
            extends,
            workers,
            supportedRuntimes));

    private static KeyValuePair<string, ProfileDefinition> Entry(string name, ProfileDefinition definition)
        => new(name, definition);

    private static ProfileDefinition Definition(
        string? hostRange = "[1.0.0, 2.0.0)",
        string? bundleRange = null,
        string? status = null,
        string? deprecationUrl = null,
        string? extends = null,
        Dictionary<string, ProfileWorkerConstraint?>? workers = null,
        List<string>? supportedRuntimes = null)
        => new()
        {
            Status = status,
            DeprecationUrl = deprecationUrl,
            Extends = extends,
            Host = hostRange is null ? null : new ProfileVersionConstraint { Version = hostRange },
            ExtensionBundle = bundleRange is null ? null : new ProfileVersionConstraint { Version = bundleRange },
            Workers = workers,
            SupportedRuntimes = supportedRuntimes,
        };

    private sealed class FakeProfileSource(
        ProfileSourceInfo source,
        IReadOnlyList<KeyValuePair<string, ProfileDefinition>> profiles) : IProfileSource
    {
        private readonly ProfileSourceInfo _source = source;
        private readonly IReadOnlyList<KeyValuePair<string, ProfileDefinition>> _profiles = profiles;

        public int LoadCount { get; private set; }

        public Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
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
