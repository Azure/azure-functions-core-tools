# Agent Skills

Skills in this directory follow the [Agent Skills](https://agentskills.io)
specification. Each skill is a directory containing a `SKILL.md` (required) plus
any optional supporting files.

## Structure

```
.github/skills/<skill-name>/
├── SKILL.md          # Required: frontmatter (name, description) + instructions
├── scripts/          # Optional: executable helpers
├── references/       # Optional: deeper documentation
└── assets/           # Optional: templates, fixtures
```

## Discovery

- **Source of truth:** `.github/skills/<name>/SKILL.md`.
- **Claude Code:** discovered automatically via `.claude/skills/<name>` symlinks
  that point back here. Do not duplicate skill content into `.claude/`.
- **Other agents (Copilot CLI, Codex, etc.):** point to skills from the root
  `AGENTS.md` so they get loaded when the agent reads project context.

## Authoring checklist

- [ ] Frontmatter `name` matches the directory name.
- [ ] `description` says what the skill does **and** when to use it. Don't
      repeat that inside the body.
- [ ] Don't restate things the agent already knows. Focus on what's specific to
      this repo.
- [ ] Prefer scripts for deterministic steps (data fetching, formatting).
- [ ] Scripts use PowerShell or .NET file-based apps, not bash, for parity with
      Windows contributors.
- [ ] When you add a new skill, add a corresponding symlink in `.claude/skills/`.
