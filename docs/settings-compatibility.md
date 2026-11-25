# Local settings compatibility

Azure Functions CLI v5 does not decrypt encrypted `local.settings.json` values.
An explicit `IsEncrypted: true` marker (including the string `"true"`) identifies
an encrypted file. The CLI does not expose its application values through the
local-settings snapshot.

- Plaintext values (`IsEncrypted: false`) and the existing missing-marker
  behavior are unchanged.
- Encrypted values cannot supply a worker-runtime fallback or be projected into
  CLI project metadata or ordinary Host application configuration.
- Reading configuration does not fail just because an encrypted file exists.
  Other safe configuration sources remain available.
- Startup rejects an encrypted file before project preparation, even when its
  values are empty or another source could replace them. The marker indicates
  that the user intends to use encrypted settings; overrides do not bypass the
  unsupported encryption boundary.
- The separate `Host` metadata projection is unchanged.

The startup diagnostic is fixed and does not include setting names or values:

> Azure Functions CLI cannot start the Functions Host while local.settings.json is encrypted. Decrypt the file before trying again, or remove it if it is no longer needed.

Use independently supported tooling to decrypt the file before startup, or
remove it if it is no longer needed. This guard does not decrypt, migrate,
rewrite, or remove local settings, access key stores, or write project
configuration. Encryption management and broader settings compatibility remain
separate work.

## Other differences from v4

- **Malformed files:** if `local.settings.json` is not valid JSON, or its root
  is not a JSON object, `func start` warns, ignores the file, and continues.
  The warning includes the parser's line and position.
- **Non-string values:** `Values` entries must be strings, as they become
  environment variables. Numbers, booleans, `null`, nested objects, and arrays
  are skipped with a single warning that lists their keys. Quote numbers and
  booleans, and store each nested value as its own string setting. v4 converted
  numbers and booleans to strings and failed to load the whole file on nested
  values.
- **Key Vault references:** values such as
  `@Microsoft.KeyVault(SecretUri=...)` are not resolved. `func start` warns and
  passes the reference text to the app unchanged. v4 resolved them with the
  signed-in Azure identity.
- **`Host.LocalHttpAddress`:** ignored with a warning; the Functions Host
  always listens on `localhost`. `LocalHttpPort`, `CORS`, and `CORSCredentials`
  are still supported.
- **File location:** `func start` reads `local.settings.json` only from the
  project folder. v4 searched parent folders; v5 does not, and the project
  itself is not resolved from a subfolder. Run `func start` from the project
  root or pass its path.
- **Missing settings:** `func start` reads `AzureWebJobsStorage` only to decide
  whether to start Azurite. v4 also checked that the storage setting and each
  binding's connection setting existed before starting the host; v5 leaves
  those errors to the host.
