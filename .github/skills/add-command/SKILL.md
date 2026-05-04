---
name: add-command
description: 'Use when adding a new top-level or subcommand to the func CLI. Walks through the file, registration, test, and doc updates required.'
---

# Add a New CLI Command

See `docs/adding-a-command.md` for the full guide. Summary:

1. Create a class in `src/Func/Commands/` extending `FuncCliCommand`.
2. Register it in `Parser.cs`.
3. Add tests in `test/Func.Tests/Commands/`.
4. Update the command tree in `docs/cli-architecture.md`.
