# template-package-install

> **DEFERRED historical lifecycle proposal.** The old `func new install/update/uninstall` command family is superseded by the future `func templates` direction. .NET workload template packaging under `content/templates` and `templateRoot` emission were implemented later through #5936/#5938; the remaining proposals here concern historical or deferred standalone package lifecycle work unless noted. Workload roots feed the engine through a read-only provider using the persistent CLI hive. Standalone acquisition will use the engine's managed provider in that same hive. Start with [Templates Architecture](../../README.md).

Preserve the earlier lifecycle research; the current workload-only slice does not implement standalone template management.
