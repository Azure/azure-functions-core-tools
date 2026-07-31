# Func-owned post-action ActionIds

This file is the single source of truth for the **ActionId GUIDs** that the
func host dispatches (design §2.6, D6/D11/D13). The templating engine has no
`IPostActionProcessor`; the host runs its own code keyed by `ActionId`
(D32). Template authors and the host-side dispatcher **must** agree on these
exact GUIDs. Unknown ActionIds fall through to their `manualInstructions`.

## Func-owned ActionId

### Append-to-app / blueprint (Python v2 append flow — §2.5, D13)

```
E715449B-264D-4669-BC62-DFC06539D969
```

- **Owner:** func host `AppendToHostFilePostActionProcessor`.
- **Declared by:** the Python `HttpTrigger` item template (and any future
  append-flow template on any stack).
- **`args`:**
  - `targetFileParam` — name of the symbol holding the target file
    (`AppFile`, hydrated from `--file`; default `function_app.py`).
  - `appObjectParam` — name of the symbol holding the decorator object
    (`AppObject`, hidden; the CLI sets `app` or `bp`).
  - `deleteStagedFile` — `"true"` to remove the staged `__snippet__.py`
    after a successful append.
- **`manualInstructions`:** non-empty, so foreign hosts (`dotnet new`, VS)
  degrade gracefully by printing them instead of appending.
- **`continueOnError`:** `"false"`.

The dispatcher resolves the target from `targetFileParam`, creates the file
with the correct header when missing (app header for `function_app.py`,
blueprint header otherwise), appends the staged snippet with separator
hygiene, guards against duplicate function names, deletes the staged file,
and reports the target as a modified output.

## Reserved engine ActionIds (NOT func-owned — for dispatcher context)

These are built into the templating engine / `dotnet new` and are listed
here only so the dispatcher and template authors do not accidentally reuse
their GUIDs. See §2.6 for which the func host re-implements.

| Purpose | ActionId | Func host behaviour |
| --- | --- | --- |
| Display manual instructions | `AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C` | Dispatched: print instructions |
| Add package/project reference | `B17581D1-C5C9-4489-8F0A-004BE667B814` | Dispatched (D11): targeted csproj XML edit |
| Open file(s) in editor | `84C0DA21-51C8-4541-9940-6CA19AF04EE6` | Not dispatched: silently skipped |
