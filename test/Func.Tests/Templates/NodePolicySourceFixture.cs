// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using NuGet.Packaging;

namespace Azure.Functions.Cli.Tests.Templates;

internal sealed class NodePolicySourceFixture : NativeTemplateSource, IDisposable
{
    private readonly TemplateCatalogEntry _entry;
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly string _configuration;
    private bool _disposed;

    internal NodePolicySourceFixture(ResolvedTemplate selected, string archive)
    {
        _entry = selected.Entry;
        using PackageArchiveReader package = new(archive);
        foreach (string path in package.GetFiles())
        {
            using Stream input = package.GetStream(path);
            using MemoryStream bytes = new();
            input.CopyTo(bytes);
            _files[path] = bytes.ToArray();
        }
        JsonObject manifest = JsonNode.Parse(_files["provenance/node/compatibility.json"])!.AsObject();
        JsonNode mapping = manifest["templates"]!.AsArray().Single(value => value!["identity"]!.GetValue<string>() == _entry.Identity)!;
        _configuration = mapping["configuration"]!["path"]!.GetValue<string>();
    }

    public override string Identity => _entry.Identity;
    public override string DisplayName => _entry.Name;
    public override bool IsProject => false;
    internal string? LanguageOverride { get; set; }
    public override string? Language => LanguageOverride ?? _entry.Language;
    public override IReadOnlyList<string> ShortNames => _entry.ShortNames;
    public override bool IsHidden => _entry.IsHidden;
    public override string? InternalRole => null;
    public override string? DefaultName => _entry.DefaultName;
    public override string PackageId => _entry.Package!.Identifier;
    public override string PackageVersion => _entry.Package!.Version!;
    public override string ConfigurationSha256 => Convert.ToHexString(SHA256.HashData(_files[_configuration]));
    public override IReadOnlyList<TemplateUserPrompt> CanonicalParameters => [.. _entry.Parameters.Select(value =>
        new TemplateUserPrompt(value.CanonicalName, value.Description, value.DataType, value.DefaultValue,
            [.. value.Choices.Select(choice => choice.Value)], value.IsRequired, null, value.ShortName, value.LongName))];

    public override Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ReadOnlyMemory<byte>>(_files[relativePath].ToArray());
    }

    public override Task RevalidateAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    internal void ChangeOriginal(Action<JsonNode> change)
    {
        Change("provenance/node/v2/templates/templates.json", root =>
            change(root.AsArray().Single(value => value!["id"]!.GetValue<string>() == "TimerTrigger-JavaScript")!));
    }

    internal void ChangeConfiguration(Action<JsonNode> change) => Change(_configuration, change);

    internal void ChangeNativeBody()
    {
        JsonNode manifest = JsonNode.Parse(_files["provenance/node/compatibility.json"])!;
        string path = manifest["templates"]!.AsArray().Single(value => value!["identity"]!.GetValue<string>() == Identity)!
            ["content"]!["path"]!.GetValue<string>();
        _files[path] = "different native body"u8.ToArray();
        RebindOwnDescriptor(path);
    }

    private void Change(string path, Action<JsonNode> change)
    {
        JsonNode document = JsonNode.Parse(_files[path])!;
        change(document);
        _files[path] = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());
        RebindOwnDescriptor(path);
    }

    private void RebindOwnDescriptor(string path)
    {
        const string manifestPath = "provenance/node/compatibility.json";
        JsonNode manifest = JsonNode.Parse(_files[manifestPath])!;
        void Update(JsonNode? value)
        {
            if (value is JsonObject obj)
            {
                if (obj["path"]?.GetValue<string>() == path && obj.ContainsKey("sha256"))
                {
                    obj["sha256"] = Convert.ToHexString(SHA256.HashData(_files[path]));
                }
                foreach (var property in obj.ToArray())
                {
                    Update(property.Value);
                }
            }
            else if (value is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    Update(item);
                }
            }
        }
        Update(manifest);
        _files[manifestPath] = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString());
    }

    public void Dispose() => _disposed = true;
}

internal sealed class NodePolicyInvocationFixture(NativeTemplateSource source, WorkingDirectory output,
    IReadOnlyDictionary<string, string?> values) : NativeTemplateInvocationContext
{
    public override NativeTemplateSource Source => source;
    public override WorkingDirectory ProjectDirectory => output;
    public override WorkingDirectory OutputDirectory => output;
    public override string? Name => "QualifiedFunction";
    public override bool DryRun => false;
    public override bool Force => false;
    public override IReadOnlyDictionary<string, string?> CanonicalValues => values;
    public override IReadOnlySet<string> ExplicitParameterNames { get; } = new HashSet<string>();
    public override NativeTemplateProjectBinding? ProjectBinding => null;
    public override Task RevalidateAsync(CancellationToken cancellationToken) => source.RevalidateAsync(cancellationToken);
}
