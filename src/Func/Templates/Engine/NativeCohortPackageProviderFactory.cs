// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class NativeTemplateCohortException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>
/// Scopes native discovery before its identity-deduplicating cache, without mutating the registered store.
/// </summary>
internal sealed class NativeCohortPackageProviderFactory(ITemplatePackageProviderFactory factory, NativeTemplatePackageScope scope)
    : ITemplatePackageProviderFactory, IPrioritizedComponent
{
    private readonly ITemplatePackageProviderFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly NativeTemplatePackageScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public Guid Id => _factory.Id;

    public string DisplayName => _factory.DisplayName;

    public int Priority => _factory is IPrioritizedComponent prioritized ? prioritized.Priority : 0;

    internal string? Failure { get; private set; }

    public ITemplatePackageProvider CreateProvider(IEngineEnvironmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ITemplatePackageProvider provider = _factory.CreateProvider(settings);
        return provider is IManagedTemplatePackageProvider managed
            ? new ManagedProvider(this, managed, settings, _scope)
            : new ScopedProvider(this, provider, settings, _scope);
    }

    private class ScopedProvider(NativeCohortPackageProviderFactory factory, ITemplatePackageProvider provider,
        IEngineEnvironmentSettings settings, NativeTemplatePackageScope scope) : ITemplatePackageProvider, IDisposable
    {
        private readonly NativeCohortPackageProviderFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        private readonly ITemplatePackageProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        private readonly IEngineEnvironmentSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        private readonly NativeTemplatePackageScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));

        public event Action? TemplatePackagesChanged
        {
            add => _provider.TemplatePackagesChanged += value;
            remove => _provider.TemplatePackagesChanged -= value;
        }

        public ITemplatePackageProviderFactory Factory => _factory;

        public async Task<IReadOnlyList<ITemplatePackage>> GetAllTemplatePackagesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IReadOnlyList<ITemplatePackage> packages = await _provider.GetAllTemplatePackagesAsync(cancellationToken);
                List<ITemplatePackage> selected = [];
                HashSet<string> exactRegistrations = new(StringComparer.OrdinalIgnoreCase);
                foreach (ITemplatePackage package in packages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TemplatePackageInfo? info = package is IManagedTemplatePackage managed ? new(managed.Identifier, managed.Version) : null;
                    if (info is not null && _scope.IsBundledFamily(info)
                        && (!NuGetVersion.TryParse(info.Version, out NuGetVersion? version)
                            || !exactRegistrations.Add($"{info.Identifier}@{version.ToNormalizedString()}")))
                    {
                        throw new NativeTemplateCohortException(
                            $"Native package '{info.Identifier}@{info.Version}' has an invalid or ambiguous version registration. Explicitly repair ownership before retrying.");
                    }
                    NativeTemplatePackageDisposition disposition = await _scope.ClassifyAsync(info, cancellationToken);
                    if (disposition.Diagnostic is { } ownershipDiagnostic)
                    {
                        throw new NativeTemplateCohortException(ownershipDiagnostic);
                    }
                    string? expected = disposition.OwnedSha256;
                    if (disposition.Include && info is not null)
                    {
                        (string? hash, string? diagnostic) = await _scope.VerifyAsync(info, cancellationToken);
                        if (diagnostic is not null)
                        {
                            throw new NativeTemplateCohortException(diagnostic);
                        }
                        expected = hash;
                    }
                    if (expected is not null && await _scope.ValidateArchiveAsync(
                        _settings.Host.FileSystem, package.MountPointUri, expected, cancellationToken) is { } packageDiagnostic)
                    {
                        throw new NativeTemplateCohortException(packageDiagnostic);
                    }
                    if (disposition.Include)
                    {
                        selected.Add(package);
                    }
                }
                return selected.AsReadOnly();
            }
            catch (NativeTemplateCohortException exception)
            {
                _factory.Failure = exception.Message;
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _factory.Failure = $"Cannot validate the captured native package cohort: {exception.Message}";
                throw new NativeTemplateCohortException(_factory.Failure, exception);
            }
        }

        public void Dispose() => (_provider as IDisposable)?.Dispose();
    }

    private sealed class ManagedProvider(NativeCohortPackageProviderFactory factory, IManagedTemplatePackageProvider provider,
        IEngineEnvironmentSettings settings, NativeTemplatePackageScope scope)
        : ScopedProvider(factory, provider, settings, scope), IManagedTemplatePackageProvider
    {
        public Task<IReadOnlyList<CheckUpdateResult>> GetLatestVersionsAsync(IEnumerable<IManagedTemplatePackage> templatePackages,
            CancellationToken cancellationToken) => Refuse<IReadOnlyList<CheckUpdateResult>>(cancellationToken);

        public Task<IReadOnlyList<UpdateResult>> UpdateAsync(IEnumerable<UpdateRequest> updateRequests, CancellationToken cancellationToken) =>
            Refuse<IReadOnlyList<UpdateResult>>(cancellationToken);

        public Task<IReadOnlyList<UninstallResult>> UninstallAsync(IEnumerable<IManagedTemplatePackage> templatePackages,
            CancellationToken cancellationToken) => Refuse<IReadOnlyList<UninstallResult>>(cancellationToken);

        public Task<IReadOnlyList<InstallResult>> InstallAsync(IEnumerable<InstallRequest> installRequests, CancellationToken cancellationToken) =>
            Refuse<IReadOnlyList<InstallResult>>(cancellationToken);

        private Task<T> Refuse<T>(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The captured native runtime view is read-only. Use an explicit template or workload lifecycle command.");
        }
    }
}
