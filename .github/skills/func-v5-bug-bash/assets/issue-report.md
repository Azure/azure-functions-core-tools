<!--
Use the exact title prefix and default labels discovered from the issue form
linked by the live bug-bash wiki. Do not assume a generic v5 prefix.
-->

## Version

- **Executable:** <!-- literal isolated CLI path; replace home directory with ~ -->
- **`--version`:** <!-- exact output -->
- **`--verbose`:**

  ```text
  <!-- output from the same executable -->
  ```

- **Host version:** <!-- relevant actual version or N/A -->
- **Workload versions:** <!-- relevant actual package/alias versions or N/A -->

## OS / shell

- **OS / architecture:** <!-- include libc on Linux -->
- **Shell:** <!-- shell name and version -->

## Stack

- **Stack / runtime:** <!-- selected stack and runtime version -->
- **Target profile:** <!-- profile name only, or N/A -->
- **Template ID:** <!-- exact template ID, or N/A -->

## Description

<!-- One or two sentences describing the specific failure, confusing behavior, feedback, or documentation gap. -->

## Steps to reproduce

1. <!-- Minimal first step using the isolated executable. -->
2. <!-- Exact next step. -->
3. <!-- Point where the behavior occurs. -->

## Expected behavior

<!-- What should have happened? -->

## Actual behavior

<!-- What happened instead? Include the exact exit code and distinguish listing, generation, startup, and invocation. -->

## Command and output

**Command**

```text
<!-- Exact command tokens, with sensitive values replaced by [REDACTED]. -->
```

**Exit code:** `<!-- integer or "not started" -->`

<details>
<summary>stdout</summary>

```text
<!-- Relevant stdout. -->
```

</details>

<details>
<summary>stderr</summary>

```text
<!-- Relevant stderr. -->
```

</details>

## Bug-bash configuration

<!-- Values are intentionally omitted. Use only set/missing/mismatch. -->

| Setting | Status |
| --- | --- |
| `FUNC_CLI_WORKLOADS_SOURCE` | <!-- status --> |
| `FUNC_CLI_QUICKSTART_MANIFEST_URL` | <!-- status --> |
| `FUNC_CLI_WORKLOADS_PRERELEASE` | <!-- status --> |
| `FUNC_CLI_HOME` | <!-- status --> |
| `FUNC_CLI_WORKLOADS_HOME` | <!-- status --> |

## Stack trace / logs

```text
<!-- Minimal relevant logs, or N/A. -->
```

## Attachments

- [ ] Evidence reviewed for secrets, customer data, and unrelated content
- [ ] No attachments
- [ ] Confirmed screenshots or recordings: <!-- filenames and alt text -->

<!-- Record unavailable assigned prerequisites or builds without substituting another version. -->
