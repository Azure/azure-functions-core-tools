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
- It has a **primary author signature**. A repository signature alone is not
  accepted. The cryptographic signature and signed package content must be valid.
- The author certificate chains to an approved root embedded in the CLI.
- The validated certificate's subject identifies Microsoft Corporation:
  `CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US`.
  Subject attributes are compared individually and exactly, not by substring,
  NuGet author metadata, or timestamp-signer identity.

The bundled anchor is DigiCert Trusted Root G4, SHA-256
`552F7BDCF1A7AF9E6CE672017F4F12ABF77240C78E761AC203D1D9D20AC89988`.
That root issues certificates to organizations other than Microsoft; the author
identity check is a separate requirement. Individual leaf certificates are not
pinned, so normal Microsoft signing-certificate rotation under this root does
not require a CLI update solely because the leaf hash changes.

A single RFC 3161 author timestamp is supported. Its signature, binding to the
author signature, certificate purpose, and chain to a bundled root must be valid.
An author certificate that has since expired can still qualify when it was valid
at the verified timestamp. An expired certificate without a qualifying timestamp
does not qualify. Multiple timestamps are rejected rather than selecting one
implicitly.

The CLI does not perform online OCSP/CRL revocation checks. NuGet author
verification uses offline revocation mode; known cached revocation failures may
still reject an archive. Repository-signature fallback and remote root-bundle
rotation are not supported.

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

`--allow-untrusted` bypasses publisher trust checks only. Package format,
resolved package ID/version, manifest, workload type, and RID compatibility
checks still apply. For a pointer install or update, the explicit choice applies
to both newly acquired archives. It is also forwarded when an interactive
`install` accepts the offer to run `update` instead.

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

## Trust-anchor maintenance

`src/Func/Workloads/Install/trusted-roots.pem` is embedded in the shipped CLI.
Changing this file changes the trust boundary and requires independent review
of the root, publisher identity policy, real signed artifacts, and timestamp
chains. The current root was corroborated against the signed Host
`4.1048.200-preview.3` pointer and `win-x64` implementation and the .NET SDK's
code-signing root bundle. Release qualification must also exercise supported
Linux and macOS environments; Windows validation alone does not establish their
certificate-chain behavior.
