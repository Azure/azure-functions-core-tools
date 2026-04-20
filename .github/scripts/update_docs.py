#!/usr/bin/env python3
"""
update_docs.py — Update the Core Tools reference documentation with CLI command changes.

Reads a diff JSON (from extract_commands.py --diff) and the existing
functions-core-tools-reference.md, then patches the doc file in place:
  - Adds new option rows to existing command tables
  - Removes option rows for deleted arguments
  - Updates descriptions for modified arguments
  - Adds new command sections for entirely new commands
  - Marks removed commands with deprecation notices

Usage:
    python update_docs.py <doc_file> <diff_json> [--dry-run]
"""

import argparse
import json
import re
import sys
from pathlib import Path


# Map command keys (from extract_commands.py) to doc section headers.
# The doc uses "func <context> <name>" format, e.g., "func durable start-new".
def command_key_to_doc_heading(key: str) -> str:
    """Convert a command key like 'Durable.start-new' to 'func durable start-new'."""
    parts = key.split(".", 1)
    if len(parts) == 2:
        context, name = parts
        if context:
            return f"func {context.lower()} {name}"
        return f"func {name}"
    return f"func {key}"


def find_command_section(lines: list[str], heading: str) -> tuple[int, int]:
    """Find the start and end line indices for a command section.

    Returns (start, end) where start is the ## heading line and end is the line
    before the next ## heading (or end of file).
    """
    pattern = re.compile(r"^##\s+`" + re.escape(heading) + r"`", re.IGNORECASE)
    start = None
    for i, line in enumerate(lines):
        if start is None:
            if pattern.match(line.strip()):
                start = i
        else:
            # Found the next ## section
            if line.strip().startswith("## "):
                return (start, i)
    if start is not None:
        return (start, len(lines))
    return (None, None)


def find_options_table(lines: list[str], start: int, end: int) -> tuple[int, int]:
    """Find the options table within a command section.

    Returns (table_start, table_end) — the first | row and last | row.
    """
    table_start = None
    table_end = None
    in_table = False
    for i in range(start, end):
        line = lines[i].strip()
        if line.startswith("| ") and "Option" in line and "Description" in line:
            table_start = i
            in_table = True
        elif in_table and line.startswith("|"):
            table_end = i + 1
        elif in_table and not line.startswith("|"):
            break
    return (table_start, table_end)


def format_option_row(arg: dict) -> str:
    """Format an argument as a markdown table row matching the doc style."""
    flag = arg.get("long", arg.get("short", "unknown"))
    desc = arg.get("description", "")
    # Clean up interpolated string artifacts
    desc = re.sub(r"\{[^}]*\}", "...", desc)
    if not desc:
        desc = f"Sets the `{flag}` option."
    return f'| **`--{flag}`** | {desc} |'


def add_option_to_table(lines: list[str], table_end: int, arg: dict) -> list[str]:
    """Insert a new option row at the end of an options table."""
    row = format_option_row(arg)
    lines.insert(table_end, row + "\n")
    return lines


def remove_option_from_table(lines: list[str], start: int, end: int, flag_name: str) -> list[str]:
    """Remove an option row from a table by flag name."""
    pattern = re.compile(r"\|\s*\*\*`--" + re.escape(flag_name) + r"`\*\*")
    for i in range(start, end):
        if pattern.search(lines[i]):
            lines.pop(i)
            return lines
    return lines


def update_option_description(lines: list[str], start: int, end: int, flag_name: str, new_desc: str) -> list[str]:
    """Update the description of an existing option in a table."""
    pattern = re.compile(r"\|\s*\*\*`--" + re.escape(flag_name) + r"`\*\*")
    for i in range(start, end):
        if pattern.search(lines[i]):
            lines[i] = f'| **`--{flag_name}`** | {new_desc} |\n'
            return lines
    return lines


def generate_new_command_section(cmd: dict) -> str:
    """Generate a complete new command section for the doc."""
    name = cmd.get("name", "unknown")
    context = cmd.get("context", "")
    help_text = cmd.get("help_text", "")
    args = cmd.get("arguments", [])

    if context:
        full_cmd = f"func {context.lower()} {name}"
    else:
        full_cmd = f"func {name}"

    section = f'\n## `{full_cmd}`\n\n'
    if help_text:
        section += f'{help_text}\n\n'

    section += f'```command\n{full_cmd}\n```\n\n'

    if args:
        section += f'`{full_cmd}` supports the following options:\n\n'
        section += '| Option     | Description                            |\n'
        section += '| ------------ | -------------------------------------- |\n'
        for arg in args:
            section += format_option_row(arg) + '\n'
        section += '\n'

    return section


def generate_deprecation_notice(cmd: dict) -> str:
    """Generate a deprecation notice for a removed command."""
    name = cmd.get("name", "unknown")
    context = cmd.get("context", "")
    if context:
        full_cmd = f"func {context.lower()} {name}"
    else:
        full_cmd = f"func {name}"
    return f'\n> [!NOTE]\n> The `{full_cmd}` command has been removed in this version.\n\n'


def apply_diff(doc_path: str, diff: dict, dry_run: bool = False) -> str:
    """Apply a command diff to the documentation file."""
    content = Path(doc_path).read_text(encoding="utf-8")
    lines = content.splitlines(keepends=True)
    changes_made = []

    # Process modified commands
    for key, changes in diff.get("modified", {}).items():
        heading = command_key_to_doc_heading(key)
        start, end = find_command_section(lines, heading)
        if start is None:
            changes_made.append(f"⚠️  Section not found for modified command: {heading}")
            continue

        if "arguments" in changes:
            arg_changes = changes["arguments"]

            # Add new arguments
            for flag_name, arg in arg_changes.get("added", {}).items():
                table_start, table_end = find_options_table(lines, start, end)
                if table_end is not None:
                    lines = add_option_to_table(lines, table_end, arg)
                    end += 1  # Adjust for inserted line
                    changes_made.append(f"✅ Added --{flag_name} to {heading}")
                else:
                    changes_made.append(f"⚠️  No options table found for {heading}, can't add --{flag_name}")

            # Remove deleted arguments
            for flag_name, arg in arg_changes.get("removed", {}).items():
                table_start, table_end = find_options_table(lines, start, end)
                if table_start is not None:
                    old_len = len(lines)
                    lines = remove_option_from_table(lines, table_start, table_end, flag_name)
                    if len(lines) < old_len:
                        end -= 1
                        changes_made.append(f"❌ Removed --{flag_name} from {heading}")
                    else:
                        changes_made.append(f"⚠️  Could not find --{flag_name} in {heading} table")

            # Update modified arguments
            for flag_name, arg_diff in arg_changes.get("modified", {}).items():
                table_start, table_end = find_options_table(lines, start, end)
                if table_start is not None:
                    new_arg = arg_diff.get("new", {})
                    new_desc = new_arg.get("description", "")
                    if new_desc:
                        lines = update_option_description(lines, table_start, table_end, flag_name, new_desc)
                        changes_made.append(f"✏️  Updated --{flag_name} description in {heading}")

        if "help_text" in changes:
            new_help = changes["help_text"]["new"]
            # Update the description paragraph after the heading
            # Look for the first non-empty line after the heading
            for i in range(start + 1, min(start + 5, end)):
                line = lines[i].strip()
                if line and not line.startswith("```") and not line.startswith("|") and not line.startswith("#"):
                    lines[i] = new_help + "\n"
                    changes_made.append(f"✏️  Updated help text for {heading}")
                    break

    # Process new commands — add at end of file (before any trailing content)
    for key, cmd in diff.get("added", {}).items():
        heading = command_key_to_doc_heading(key)
        # Check if section already exists
        start, _ = find_command_section(lines, heading)
        if start is not None:
            changes_made.append(f"⚠️  Section already exists for new command: {heading}")
            continue

        section = generate_new_command_section(cmd)
        # Find the right insertion point — before the last line or related-content section
        insert_at = len(lines)
        for i in range(len(lines) - 1, -1, -1):
            if lines[i].strip().startswith("## "):
                insert_at = i
                break
            elif lines[i].strip().startswith("## Related content") or lines[i].strip().startswith("## Next steps"):
                insert_at = i
                break

        for j, section_line in enumerate(section.splitlines(keepends=True)):
            if not section_line.endswith("\n"):
                section_line += "\n"
            lines.insert(insert_at + j, section_line)

        changes_made.append(f"✅ Added new section for {heading}")

    # Process removed commands — add deprecation notice
    for key, cmd in diff.get("removed", {}).items():
        heading = command_key_to_doc_heading(key)
        start, end = find_command_section(lines, heading)
        if start is None:
            changes_made.append(f"⚠️  Section not found for removed command: {heading}")
            continue

        notice = generate_deprecation_notice(cmd)
        # Insert deprecation notice right after the heading
        for j, notice_line in enumerate(notice.splitlines(keepends=True)):
            if not notice_line.endswith("\n"):
                notice_line += "\n"
            lines.insert(start + 1 + j, notice_line)

        changes_made.append(f"❌ Added deprecation notice for {heading}")

    result = "".join(lines)

    # Update ms.date in frontmatter
    from datetime import date
    today = date.today().strftime("%m/%d/%Y")
    result = re.sub(r"ms\.date:\s*\d{2}/\d{2}/\d{4}", f"ms.date: {today}", result)

    if not dry_run:
        Path(doc_path).write_text(result, encoding="utf-8")

    # Print summary
    if changes_made:
        print("Changes applied:" if not dry_run else "Changes that would be applied:")
        for change in changes_made:
            print(f"  {change}")
    else:
        print("No changes to apply.")

    return result


def main():
    parser = argparse.ArgumentParser(description="Update Core Tools reference docs from a command diff")
    parser.add_argument("doc_file", help="Path to functions-core-tools-reference.md")
    parser.add_argument("diff_json", help="Path to diff JSON from extract_commands.py")
    parser.add_argument("--dry-run", action="store_true", help="Preview changes without writing")
    args = parser.parse_args()

    with open(args.diff_json) as f:
        diff = json.load(f)

    if not diff.get("has_changes"):
        print("No changes in diff — nothing to update.")
        return

    apply_diff(args.doc_file, diff, dry_run=args.dry_run)


if __name__ == "__main__":
    main()
