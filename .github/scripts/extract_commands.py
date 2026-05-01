#!/usr/bin/env python3
"""
extract_commands.py — Extract CLI command metadata from azure-functions-core-tools source.

Parses command definitions from C# files in src/Func/Commands/.
Outputs a JSON manifest of all commands and their arguments.

TODO: This script still uses v4's [Action(...)] attribute parsing logic.
It needs to be rewritten to parse v5's System.CommandLine command definitions
(e.g. Command/Option/Argument registrations) before doc-sync can work again.

Usage:
    python extract_commands.py <repo_root> [--output commands.json]
    python extract_commands.py <repo_root> --diff <old_manifest.json> [--output diff.json]
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path


def parse_action_attributes(content: str, file_path: str) -> list[dict]:
    """Extract [Action(...)] attribute metadata from a C# file."""
    actions = []

    # Match [Action(...)] - handles multiline
    pattern = r'\[Action\((.*?)\)\]'
    for match in re.finditer(pattern, content, re.DOTALL):
        attr_body = match.group(1)
        action = {"file": file_path}

        # Extract named properties
        for prop in ["Name", "Context", "SubContext", "HelpText", "ParentCommandName"]:
            prop_match = re.search(
                rf'{prop}\s*=\s*(?:Context\.)?("(?:[^"\\]|\\.)*?"|[\w.]+)',
                attr_body
            )
            if prop_match:
                val = prop_match.group(1).strip('"')
                action[prop.lower()] = val

        # ShowInHelp
        show_match = re.search(r'ShowInHelp\s*=\s*(true|false)', attr_body, re.IGNORECASE)
        action["show_in_help"] = show_match.group(1).lower() == "true" if show_match else True

        # HelpOrder
        order_match = re.search(r'HelpOrder\s*=\s*(\d+)', attr_body)
        action["help_order"] = int(order_match.group(1)) if order_match else 100

        if "name" in action:
            actions.append(action)

    return actions


def parse_arguments(content: str) -> list[dict]:
    """Extract .Setup<T>() argument definitions from ParseArgs methods."""
    args = []

    # Match Parser.Setup<Type>('short', "long") or Parser.Setup<Type>("long")
    setup_pattern = (
        r'\.Setup<(\w+)>\s*\('
        r"(?:'(\w)'(?:\s*,\s*)?)?"
        r'(?:\"([\w-]+)\")?\)'
    )

    # Find each Setup call and its chained methods
    for match in re.finditer(setup_pattern, content):
        arg = {
            "type": match.group(1),
        }
        if match.group(2):
            arg["short"] = match.group(2)
        if match.group(3):
            arg["long"] = match.group(3)

        # Look ahead for .WithDescription("...") and .SetDefault(...)
        rest = content[match.end():match.end() + 500]

        desc_match = re.search(r'\.WithDescription\(\s*"((?:[^"\\]|\\.)*?)"', rest)
        if desc_match:
            arg["description"] = desc_match.group(1)

        # Also handle interpolated/concatenated descriptions
        if not desc_match:
            desc_match = re.search(r'\.WithDescription\(\s*\$?"((?:[^"\\]|\\.)*?)"', rest)
            if desc_match:
                arg["description"] = desc_match.group(1)

        default_match = re.search(r'\.SetDefault\(\s*(.+?)\s*\)', rest)
        if default_match:
            arg["default"] = default_match.group(1).strip('"')

        if arg.get("long") or arg.get("short"):
            args.append(arg)

    return args


def extract_commands(repo_root: str) -> dict:
    """Walk the Commands directory and extract all command metadata."""
    commands_dir = Path(repo_root) / "src" / "Func" / "Commands"
    if not commands_dir.exists():
        print(f"Error: Commands directory not found at {commands_dir}", file=sys.stderr)
        sys.exit(1)

    commands = {}

    for cs_file in sorted(commands_dir.rglob("*.cs")):
        content = cs_file.read_text(encoding="utf-8-sig")
        rel_path = str(cs_file.relative_to(repo_root))
        actions = parse_action_attributes(content, rel_path)

        if not actions:
            continue

        arguments = parse_arguments(content)

        for action in actions:
            # Build a unique key: context.name or just name
            context = action.get("context", "")
            name = action.get("name", "")
            key = f"{context}.{name}" if context else name

            if key not in commands:
                commands[key] = {
                    "name": name,
                    "context": context,
                    "help_text": action.get("helptext", ""),
                    "show_in_help": action.get("show_in_help", True),
                    "help_order": action.get("help_order", 100),
                    "parent_command": action.get("parentcommandname", ""),
                    "file": action.get("file", ""),
                    "arguments": arguments,
                }
            else:
                # Merge: some commands have multiple [Action] attributes
                existing = commands[key]
                if not existing["help_text"] and action.get("helptext"):
                    existing["help_text"] = action["helptext"]

    return commands


def diff_manifests(old_manifest: dict, new_manifest: dict) -> dict:
    """Diff two command manifests. Returns added, removed, and modified commands."""
    old_keys = set(old_manifest.keys())
    new_keys = set(new_manifest.keys())

    added = {k: new_manifest[k] for k in (new_keys - old_keys)}
    removed = {k: old_manifest[k] for k in (old_keys - new_keys)}

    modified = {}
    for k in old_keys & new_keys:
        old_cmd = old_manifest[k]
        new_cmd = new_manifest[k]

        changes = {}

        # Check help text changes
        if old_cmd.get("help_text") != new_cmd.get("help_text"):
            changes["help_text"] = {
                "old": old_cmd.get("help_text", ""),
                "new": new_cmd.get("help_text", ""),
            }

        # Check argument changes
        old_args = {a.get("long", a.get("short", "")): a for a in old_cmd.get("arguments", [])}
        new_args = {a.get("long", a.get("short", "")): a for a in new_cmd.get("arguments", [])}

        added_args = {k: new_args[k] for k in (set(new_args) - set(old_args))}
        removed_args = {k: old_args[k] for k in (set(old_args) - set(new_args))}

        modified_args = {}
        for ak in set(old_args) & set(new_args):
            if old_args[ak] != new_args[ak]:
                modified_args[ak] = {"old": old_args[ak], "new": new_args[ak]}

        if added_args or removed_args or modified_args:
            changes["arguments"] = {
                "added": added_args,
                "removed": removed_args,
                "modified": modified_args,
            }

        # Check show_in_help changes
        if old_cmd.get("show_in_help") != new_cmd.get("show_in_help"):
            changes["show_in_help"] = {
                "old": old_cmd.get("show_in_help"),
                "new": new_cmd.get("show_in_help"),
            }

        if changes:
            changes["name"] = new_cmd.get("name", k)
            changes["context"] = new_cmd.get("context", "")
            modified[k] = changes

    return {
        "added": added,
        "removed": removed,
        "modified": modified,
        "has_changes": bool(added or removed or modified),
        "summary": {
            "added_count": len(added),
            "removed_count": len(removed),
            "modified_count": len(modified),
        },
    }


def generate_change_summary(diff: dict) -> str:
    """Generate a human-readable markdown summary of command changes."""
    lines = ["## Azure Functions Core Tools — Command Changes\n"]

    if not diff["has_changes"]:
        lines.append("No command changes detected.\n")
        return "\n".join(lines)

    s = diff["summary"]
    lines.append(f"**{s['added_count']}** added · **{s['removed_count']}** removed · **{s['modified_count']}** modified\n")

    if diff["added"]:
        lines.append("### ✅ New Commands\n")
        for key, cmd in sorted(diff["added"].items()):
            ctx = f"`{cmd['context']}` → " if cmd.get("context") else ""
            lines.append(f"- {ctx}**`{cmd['name']}`** — {cmd.get('help_text', 'No description')}")
            if cmd.get("arguments"):
                for arg in cmd["arguments"]:
                    flag = f"--{arg['long']}" if arg.get("long") else f"-{arg['short']}"
                    lines.append(f"  - `{flag}`: {arg.get('description', 'No description')}")
        lines.append("")

    if diff["removed"]:
        lines.append("### ❌ Removed Commands\n")
        for key, cmd in sorted(diff["removed"].items()):
            ctx = f"`{cmd['context']}` → " if cmd.get("context") else ""
            lines.append(f"- {ctx}**`{cmd['name']}`** — {cmd.get('help_text', '')}")
        lines.append("")

    if diff["modified"]:
        lines.append("### ✏️ Modified Commands\n")
        for key, changes in sorted(diff["modified"].items()):
            ctx = f"`{changes['context']}` → " if changes.get("context") else ""
            lines.append(f"- {ctx}**`{changes['name']}`**")

            if "help_text" in changes:
                lines.append(f"  - Help text changed:")
                lines.append(f"    - Old: {changes['help_text']['old']}")
                lines.append(f"    - New: {changes['help_text']['new']}")

            if "arguments" in changes:
                arg_changes = changes["arguments"]
                if arg_changes.get("added"):
                    for name, arg in arg_changes["added"].items():
                        lines.append(f"  - New argument `--{name}`: {arg.get('description', '')}")
                if arg_changes.get("removed"):
                    for name, arg in arg_changes["removed"].items():
                        lines.append(f"  - Removed argument `--{name}`")
                if arg_changes.get("modified"):
                    for name, arg in arg_changes["modified"].items():
                        lines.append(f"  - Modified argument `--{name}`")
        lines.append("")

    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Extract CLI command metadata from azure-functions-core-tools")
    parser.add_argument("repo_root", help="Path to the repository root")
    parser.add_argument("--output", "-o", help="Output file path (default: stdout)")
    parser.add_argument("--diff", "-d", help="Path to old manifest JSON to diff against")
    parser.add_argument("--summary", "-s", action="store_true", help="Output markdown summary (only with --diff)")
    args = parser.parse_args()

    if args.diff:
        with open(args.diff) as f:
            old_manifest = json.load(f)
        new_manifest = extract_commands(args.repo_root)
        result = diff_manifests(old_manifest, new_manifest)

        if args.summary:
            output = generate_change_summary(result)
        else:
            output = json.dumps(result, indent=2)
    else:
        commands = extract_commands(args.repo_root)
        output = json.dumps(commands, indent=2)

    if args.output:
        Path(args.output).write_text(output)
        print(f"Output written to {args.output}", file=sys.stderr)
    else:
        print(output)


if __name__ == "__main__":
    main()
