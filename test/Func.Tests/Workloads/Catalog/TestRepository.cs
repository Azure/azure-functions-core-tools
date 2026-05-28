// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

/// <summary>
/// Builds a <see cref="SourceRepository"/> that hands out the supplied
/// <see cref="INuGetResource"/> instances when asked, so tests don't have
/// to spin up the real v3 provider chain.
/// </summary>
internal static class TestRepository
{
    public static SourceRepository Build(PackageSource source, params INuGetResource?[] resources)
    {
        var providers = new List<Lazy<INuGetResourceProvider>>();
        foreach (INuGetResource? resource in resources)
        {
            if (resource is null)
            {
                continue;
            }

            providers.Add(new Lazy<INuGetResourceProvider>(() => new StaticResourceProvider(resource)));
        }

        return new SourceRepository(source, providers);
    }

    private sealed class StaticResourceProvider(INuGetResource resource) : INuGetResourceProvider
    {
        public Type ResourceType { get; } = GetTopMostResourceType(resource.GetType());

        public string Name => $"Static({ResourceType.Name})";

        public IEnumerable<string> Before => [];

        public IEnumerable<string> After => [];

        public Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
            => Task.FromResult(Tuple.Create<bool, INuGetResource?>(true, resource));

        // NSubstitute's proxy is a derived class. Walk up to the abstract
        // resource base (e.g., PackageSearchResource) so the repository can
        // match GetResourceAsync<T>() requests.
        private static Type GetTopMostResourceType(Type type)
        {
            Type current = type;
            while (current.BaseType is { } baseType
                && baseType != typeof(object)
                && typeof(INuGetResource).IsAssignableFrom(baseType))
            {
                current = baseType;
            }

            return current;
        }
    }
}
