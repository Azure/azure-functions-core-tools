// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/TemplateDiscoveryCommand.cs
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

using System.CommandLine;
using Azure.Functions.Cli.TemplateDiscovery;

var outputOption = new Option<DirectoryInfo>("--output", "-o")
{
    Description = "Base directory for the generated SearchCache output.",
    Required = true,
};

var packagesPathOption = new Option<DirectoryInfo>("--packages-path")
{
    Description = "Scan pre-downloaded .nupkg files in this directory with no network access. Takes precedence over --feed.",
};
packagesPathOption.AcceptExistingOnly();

var feedOption = new Option<string>("--feed")
{
    Description = "Feed to index. Either a V3 NuGet feed URL or a local directory of .nupkg files (a directory feed is scanned offline).",
};

var packageTypeOption = new Option<string[]>("--package-type")
{
    Description = "NuGet package type(s) that identify func template packages. Used to query a remote --feed.",
    Arity = ArgumentArity.OneOrMore,
    AllowMultipleArgumentsPerToken = true,
    DefaultValueFactory = _ => ["FuncItemTemplates", "FuncAppTemplates"],
};

var noDiffOption = new Option<bool>("--no-diff")
{
    Description = "Rescan every package instead of carrying unchanged packages over from the previous run.",
};

var prereleaseOption = new Option<bool>("--prerelease")
{
    Description = "Include prerelease packages when querying a remote --feed.",
};

var noTemplateJsonFilterOption = new Option<bool>("--no-template-json-filter")
{
    Description = "Do not prefilter packages that contain no template.json files (the prefilter is on by default).",
};

var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Verbose per-package output.",
};

var rootCommand = new RootCommand("Builds the Azure Functions CLI template search index (NuGetTemplateSearchInfoVer2.json).");
rootCommand.Options.Add(outputOption);
rootCommand.Options.Add(packagesPathOption);
rootCommand.Options.Add(feedOption);
rootCommand.Options.Add(packageTypeOption);
rootCommand.Options.Add(noDiffOption);
rootCommand.Options.Add(prereleaseOption);
rootCommand.Options.Add(noTemplateJsonFilterOption);
rootCommand.Options.Add(verboseOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var options = new DiscoveryOptions(
        OutputPath: parseResult.GetValue(outputOption)!,
        PackagesPath: parseResult.GetValue(packagesPathOption),
        Feed: parseResult.GetValue(feedOption),
        PackageTypes: parseResult.GetValue(packageTypeOption) ?? ["FuncItemTemplates", "FuncAppTemplates"],
        Diff: !parseResult.GetValue(noDiffOption),
        IncludePrerelease: parseResult.GetValue(prereleaseOption),
        NoTemplateJsonFilter: parseResult.GetValue(noTemplateJsonFilterOption),
        Verbose: parseResult.GetValue(verboseOption));

    IPackageProvider? provider = null;
    try
    {
        provider = CreateProvider(options);
        Directory.CreateDirectory(options.OutputPath.FullName);
        var runner = new DiscoveryRunner(new PackageScanner(new TemplateEngineHostFactory()), new SearchCacheStore());
        return await runner.RunAsync(options, provider, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Index build cancelled.");
        return 130;
    }
    catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidOperationException or ArgumentException)
    {
        Console.Error.WriteLine($"[error] {ex.Message}");
        return 1;
    }
    finally
    {
        (provider as IDisposable)?.Dispose();
    }
});

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return await rootCommand.Parse(args).InvokeAsync(cancellationToken: cts.Token);

static IPackageProvider CreateProvider(DiscoveryOptions options)
{
    if (options.PackagesPath is not null)
    {
        return new DirectoryPackageProvider(options.PackagesPath);
    }

    if (!string.IsNullOrWhiteSpace(options.Feed))
    {
        return Directory.Exists(options.Feed)
            ? new DirectoryPackageProvider(new DirectoryInfo(options.Feed))
            : new NuGetFeedPackageProvider(options.Feed, options.PackageTypes, options.IncludePrerelease);
    }

    throw new ArgumentException("Specify a package source: --packages-path <dir> for offline scanning, or --feed <url|dir>.");
}
