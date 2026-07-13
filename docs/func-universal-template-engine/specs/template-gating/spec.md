## ADDED Requirements

### Requirement: template.json constraints are the source of truth for gating

The CLI SHALL determine template visibility solely from the `constraints` block of each
`template.json`, evaluated by Microsoft.TemplateEngine. The CLI MUST NOT gate templates
using a separate sidecar manifest, package prerelease label, or channel-match step.

#### Scenario: Gating derives from constraints

- **WHEN** the CLI lists templates for a resolved context
- **THEN** a template is visible if and only if its `template.json` constraints are satisfied

#### Scenario: Prerelease label is not authoritative

- **WHEN** a template package carries a `-preview` prerelease label
- **THEN** the label does not by itself hide or show any template; only the per-template constraints do

### Requirement: Extension-bundle requirement is a custom constraint

The CLI SHALL register a custom `func-extension-bundle` constraint that gates a template
on the project's resolved extension bundle id and version range. Bundle mismatch SHALL
render the template restricted (hidden), not a hard scaffold error.

#### Scenario: Bundle satisfies the constraint

- **WHEN** a template requires bundle version `[4.0.0,)` and the project's `host.json` bundle satisfies it
- **THEN** the template is visible and can be scaffolded

#### Scenario: Bundle too low hides the template

- **WHEN** a template requires bundle version `[4.6.0,)` and the project's bundle is `4.2.0`
- **THEN** the template is hidden from selection

### Requirement: Stack and language are selection tags, not hard constraints

The CLI SHALL treat stack and language as soft selection metadata (tags), not as hard
`constraints`. A stack mismatch SHALL NOT render a template "restricted"; it is resolved
by selection and overridable by the user.

#### Scenario: Wrong-stack template is not blocked by a constraint

- **WHEN** a user explicitly requests a Node variant inside a Python project via `--language`
- **THEN** the template is not treated as restricted by a stack constraint and scaffolding proceeds

### Requirement: Unresolvable bundle assumes latest stable

The CLI SHALL evaluate bundle constraints against a synthetic context of the latest
stable extension bundle when the project's extension bundle cannot be resolved (for
example, an empty directory with no `host.json`).

#### Scenario: Empty directory assumes latest stable

- **WHEN** a user scaffolds in an empty directory and a template declares a stable-bundle constraint
- **THEN** the constraint is evaluated against the latest stable bundle and the template is eligible

#### Scenario: Preview-only templates hidden without a project

- **WHEN** a user scaffolds in an empty directory and a template requires a preview-channel bundle id
- **THEN** the template is hidden because the assumed context is the stable bundle

### Requirement: Gating failures are ranked and surfaced with a call to action

When no template is selectable, the CLI SHALL rank the per-constraint results so that a
soft stack mismatch stays silent while a hard bundle gate on an otherwise-matching
variant is surfaced with its call-to-action message.

#### Scenario: Bundle gate call-to-action is shown

- **WHEN** the only stack-matching variant is hidden solely by a bundle constraint
- **THEN** the CLI surfaces the constraint's call-to-action (e.g. update the `host.json` bundle range)

#### Scenario: Wrong-stack mismatch stays quiet

- **WHEN** the requested id only has variants for other stacks
- **THEN** the CLI does not surface those variants' stack mismatches as actionable errors
