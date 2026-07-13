## ADDED Requirements

### Requirement: A single template id works across all stacks

The CLI SHALL group per-stack template variants under a shared `groupIdentity` and a
shared `shortName`, so that a single `func new <shortName>` invocation resolves to the
appropriate variant for the current stack without the user naming the stack.

#### Scenario: Same command in different stacks

- **WHEN** a user runs `func new httptrigger` in a Node project and again in a Python project
- **THEN** the CLI selects the Node variant in the first case and the Python variant in the second

### Requirement: Stack is resolved from explicit input then ambient context

The CLI SHALL resolve the target stack in order: (1) explicit `--language` input,
(2) inference from the working directory, (3) otherwise a hard error. The stack of a
selected template SHALL be derived from the winning variant's metadata, never from a
hardcoded language-to-stack table.

#### Scenario: Stack inferred from the directory

- **WHEN** a user runs `func new httptrigger` in a directory whose project indicates the Python stack
- **THEN** the CLI resolves the stack as Python and selects the Python variant

#### Scenario: Stack supplied by --language in an empty directory

- **WHEN** a user runs `func new httptrigger --language ts` in an empty directory
- **THEN** the CLI resolves the stack as Node from the surviving `ts` variant and scaffolds it

#### Scenario: No stack resolvable

- **WHEN** a user runs `func new httptrigger` in an empty directory with no `--language`
- **THEN** the CLI fails with an error directing the user to run `func init` or pass `--language`

### Requirement: Language selects the variant within a stack

The CLI SHALL support only `--language` as the explicit selection override and SHALL
NOT provide a `--stack` flag. When multiple variants remain after stack resolution, the
CLI SHALL resolve the language from ambient project signals, then honor `--language`,
and only prompt when the choice is still ambiguous.

#### Scenario: Ambient language resolves the tiebreak

- **WHEN** a Node project's configuration indicates TypeScript and the user runs `func new httptrigger`
- **THEN** the CLI selects the TypeScript variant without prompting

#### Scenario: Prompt only when ambiguous

- **WHEN** stack resolves to Node but neither ambient signals nor `--language` determine js vs ts
- **THEN** the CLI prompts the user to choose the language

### Requirement: Explicit language overrides ambient stack without blocking

The CLI SHALL proceed with the explicitly requested variant and SHALL emit a
non-blocking advisory note rather than an error when `--language` implies a stack
different from the ambient project stack.

#### Scenario: Cross-stack language is allowed with a note

- **WHEN** a user runs `func new httptrigger --language ts` inside a Python project
- **THEN** the CLI scaffolds the Node/TypeScript variant
- **AND** prints a non-blocking advisory that a TypeScript (Node) function is being added to a Python project

### Requirement: Selection fails when no variant fits

The CLI SHALL fail when no template variant survives stack, language, and gating
resolution for the requested id.

#### Scenario: No fitting variant

- **WHEN** the requested id has no variant applicable to the resolved stack and satisfying its gates
- **THEN** the CLI fails and reports why no template was selected
