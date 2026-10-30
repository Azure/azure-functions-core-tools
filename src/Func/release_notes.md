## 5.0.0-preview.5

# Azure Functions CLI 5.0.0

#### Changes

- Verify publisher trust and signed content before extracting newly acquired workload archives, including RID pointers and implementations. Explicit `workload install` / `update --allow-untrusted` supports unsigned development packages without bypassing workload format checks. Existing installed-content reuse and `FUNC_HOST_CONTENT_ROOT` remain unchanged. (#5759)
- Include Java and PowerShell stacks in `func setup`, and correctly infer Go setup requirements from the `native` worker runtime. (#5801)
- Fix npm networking during project initialization. (#5771)
- Update built-in profiles to host `4.1054.200` and current worker versions. (#5775)
- Rename the `func start` rendering option from `--output` to `--format`. (#5735)

## 5.0.0-preview.4

#### Changes

- Add the public `IProjectStack` Abstractions contract and an installed stack metadata catalog without changing existing `func init` scaffolding. The .NET, Node.js, Python, Go, Java, and PowerShell 1.0.0-preview.2 stack binaries require Azure Functions CLI 5.0.0-preview.4 or later; older CLIs lack this contract and cannot safely load those binaries.  (#5707)
- Enable metadata-gated `func update` for installations that explicitly support in-place updates. Updates default to the latest stable release; use `--prerelease` for previews, `--version` for a specific version (including an explicit downgrade), and `--yes` to skip confirmation. Externally managed installations receive installer/package-manager guidance without contacting the update feed; missing or invalid installation metadata produces reinstall guidance. Update invocations skip first-run setup and version advisories. (#5702)
- Add installer-authored installation metadata and validation for installation channel and update policy. Standalone installers publish metadata atomically and restore payloads and wrappers when publication fails. (#5700, #5701)
- Reject extension bundles that are incompatible with the selected profile in `func new`. (#5580)
- Remove Go worker references from non-Flex built-in profiles and Python worker references from Windows built-in profiles. Reject unsupported stacks before worker resolution or installation; upstream host and worker versions are unchanged. (#5507)
