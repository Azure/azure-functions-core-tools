# Workload publisher trust

Azure Functions CLI verifies every newly acquired workload archive before
extracting its workload metadata or payload. This applies to local and catalog
installations, updates, RID pointers, and their platform-specific implementations.
Automatic acquisitions made by commands such as `func setup` and `func start`
use the same strict policy.

## Default policy

An archive must satisfy all of these requirements:

- Its NuGet package ID starts with `azure.functions.cli.workloads.`
  (case-insensitive).
- It has a **primary NuGet author signature**. A repository signature alone is
  not accepted.
- NuGet verifies the package content integrity, signature, signing certificate
  chain, and any RFC 3161 timestamp. At most one timestamp is accepted; a
  timestamp is optional, but an invalid timestamp is not.
- The author signing certificate's SHA-256 fingerprint is on the CLI's
  Microsoft publisher allowlist. The allowlist is scoped to the primary author
  signature; a valid certificate chain or a Microsoft-looking subject alone
  does not authorize a publisher.

The allowlist contains Microsoft first-party signing fingerprints recognized
by the .NET SDK's NuGet verification policy. The current Azure Functions Host
`4.1048.200-preview.3` package was independently verified with `dotnet nuget
verify`; its author certificate matches
`9A1B131BEE0605433056A4EA3815478A8E177961A968C6C0027C1093D1FEB630`.
Certificates rotate, so adding a fingerprint requires confirming it against a
trusted Microsoft-signed Functions workload artifact and updating this policy.

Certificate-chain and revocation behavior is delegated to NuGet and the
platform's certificate facilities, with online revocation checks enabled and
unknown revocation status rejected. Verification can therefore require access
to certificate-status endpoints. If this prevents an installation, users may
explicitly use `--allow-untrusted`; the CLI never switches to that mode
automatically. The CLI does not maintain a separate root bundle, custom chain
builder, or subject-name policy. Certificate-chain behavior is platform
dependent, so release qualification must exercise supported Windows, Linux, and
macOS environments with signed workload packages.

## Explicit development installs and updates

Locally built packages are normally unsigned. Inspect the source and package
before explicitly opting out:

```console
func workload install ./My.Workload.1.0.0.nupkg --allow-untrusted
func workload install My.Workload --source https://example.test/v3/index.json --exact --allow-untrusted
func workload update My.Workload --source https://example.test/v3/index.json --allow-untrusted
func workload update --all --source https://example.test/v3/index.json --allow-untrusted
```

Catalog sources must be HTTP(S) NuGet V3 service indexes. For an archive on disk,
pass the `.nupkg` path directly to `workload install`; a local directory is not a
supported catalog source.

`--allow-untrusted` bypasses publisher authorization and certificate validation,
and allows unsigned or non-official package IDs. NuGet still verifies the
content integrity of a signed archive. Package format, resolved package
ID/version, manifest, workload type, and RID compatibility checks still apply.
For a pointer install or update, the explicit choice applies to both newly
acquired archives. It is also forwarded when an interactive `install` accepts
the offer to run `update` instead.

There is no trust-bypass option on consuming commands such as `func start`.
Preinstall trusted development content explicitly, then run the consuming
command. `FUNC_HOST_CONTENT_ROOT` still supports the existing local host-source
development workflow.

## Existing installs and local files

Compatible installed-content reuse remains unchanged. Existing installed files
are not retroactively verified, and reuse is **not** a claim that those files
have passed this policy. No provenance database is created.

Newly acquired archives are copied into a private temporary directory and the
copy is retained through verification, inspection, and deployment. On Windows,
the retained file handle also denies write/delete sharing. On Unix, the
directory is created with owner-only permissions. Replacing the original file
after acquisition cannot change the snapshot being installed. This is not a
defense against malicious code already running with the same user privileges.

User-owned source archives are never removed. Temporary snapshots are disposed
on success, failure, and cancellation; cleanup is best effort if the filesystem
refuses removal. Verification failure occurs before deployment, so an existing
installed version remains unchanged.
