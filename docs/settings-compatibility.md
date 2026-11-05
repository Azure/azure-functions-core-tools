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

> Encrypted local.settings.json files are not supported. Decrypt the file before starting the Functions Host with Azure Functions CLI, or remove the file if it is no longer needed.

Use independently supported tooling to decrypt the file before startup, or
remove it if it is no longer needed. This guard does not decrypt, migrate,
rewrite, or remove local settings, access key stores, or write project
configuration. Encryption management and broader settings compatibility remain
separate work.
