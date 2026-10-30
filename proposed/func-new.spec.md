# `func new`

## Purpose

`func new` creates a function from an installed item template in an existing
Azure Functions project. Template discovery and project creation use the single
Microsoft Template Engine integration under `src/Func/Templates/Engine`.

## Current command behavior

The template engine integration currently creates a command-scoped engine
session and lists installed templates as func-owned catalog entries. Catalog
entries include the template identity, names, type, language, package
information, and projected func host metadata and parameter definitions.

The `func new` command is not yet connected to template resolution, argument
parsing, selection, or invocation. Until those steps are implemented, creation
and `--list` fail with a diagnostic and nonzero exit code before changing
project files or configuration.

## Template model

- Microsoft Template Engine owns template discovery and package registration.
- Templates declare their kind through `tags.type`; `item` identifies function
  templates and `project` identifies project templates.
- Func projects template metadata and parameter definitions into its own
  catalog models. Raw Template Engine types remain inside the integration.
- Func host context and constraint declarations are defined by the
  integration contracts in `docs/template-engine-integration` and
  `docs/template-engine-constraints`.
- One command-scoped engine session is used for operations within a command.
  The shared func settings hive retains installed package state.

## Command integration

The command execution design in `docs/func-new-execution` covers project
context resolution, candidate filtering, strict template-parameter parsing,
selection, preview, and invocation. The command should enable creation only
when each step uses the same resolved context and engine session. `--force`
controls file-conflict behavior; it does not bypass template selection,
validation, or compatibility checks.

Package lifecycle behavior is specified separately in
`docs/template-package-install`. It shares the Template Engine integration but
does not participate in project resolution or template invocation.
