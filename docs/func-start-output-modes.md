# `func run` output modes

`func run` (also available as `func start`) renders the same host event stream
as `compact`, `plain`, or `json`.

## Choosing a mode

```powershell
func run --format=compact
func run --format=plain
func run --format=json
```

`--no-tui` selects plain output when no explicit `--format` is supplied.
An explicit format takes precedence. JSON is never selected automatically.

| Environment | Automatic mode |
|---|---|
| Supported interactive terminal | `compact` |
| Redirected input/output, CI, or unavailable full-screen support | `plain` |

Compact mode requires interactive input, ANSI output, an alternate screen
buffer, and a supported terminal input adapter. An unsafe compact request
falls back to plain with a notice on stderr. Disabling colors does not by
itself disable full-screen navigation.

## `compact`

Compact mode uses the terminal's alternate screen with a fixed header,
scrollable log pane, and footer. It does not erase the shell's scrollback.
Initialization remains outside full-screen mode: startup steps and prompts
finish before the dashboard takes ownership of terminal input. Completed
initialization steps are replayed into the dashboard before host logs.

If startup used managed interactive prompt input on Unix, the run stays in plain
mode with a notice rather than risk losing bytes buffered by the prompt reader.
Windows transfers pending prompt keys before acquiring native input. A later run
that does not need those startup prompts can use full-screen mode normally.

The labelled **Logs** divider shows live/paused state, unread entries, selection
and copy status, and active filters. Aggregate counts sit in the main banner.
The footer remains one row high and shows shortcuts for the current context:
scrolling, selection, search, function browsing, terminal selection, or shutdown.
As the window narrows, lower-priority shortcuts disappear as whole items rather
than being cut off or wrapped. A stop action remains visible; `?` opens the full
keyboard reference, CLI version, and complete copy/status feedback outside search.
Changing footer hints does not move the log viewport.

Help is height-bounded. When feedback or shortcuts do not fit, use Up/Down,
Page Up/Page Down, Home/End, or the mouse wheel over the help panel to scroll.
The help heading shows the visible range, so full copy feedback and every
shortcut remain accessible without resizing the terminal.

### Reading logs

| Input | Action |
|---|---|
| Mouse wheel / trackpad over the log pane | Scroll log rows |
| Up / Down | Scroll one visual row |
| Page Up / Page Down | Scroll a page |
| Home | Go to the oldest retained visible logs |
| End | Clear selection and return to live following |
| `c` | Clear retained dashboard logs, not the log file or counters |
| `e` | Toggle errors-only filtering |
| `1` / `2` / `3` | Select info / warning / error minimum level |
| `f` / `a` | Cycle the function filter / show all functions |
| `t` / `/` | Browse / search functions |
| `?` | Show controls |

Scrolling away from the live tail keeps the reading position anchored while
new logs arrive. The Logs divider shows unread matching entries. Selection also
pauses following. Resize reflows retained text without changing its logical
identity.

History is bounded by both **10,000 display entries** and **16 MiB of retained
display text**. Text accounting includes owned text and filtering metadata
as UTF-16 payload bytes, not total process memory. Entries larger than the
display budget are visibly truncated. Old entries are evicted when necessary;
the Logs divider indicates discarded history.
Evicted display text is released even while terminal selection pauses redraws;
the frozen terminal image does not keep a second history in the CLI.

These are display limits only. `--log-file=<path>` continues receiving events
independently of filters, scrolling, selection, clear, and display truncation.
Use it when the full run must remain available.

### Selecting and copying

Drag the left mouse button in the log pane to select text. Selection can span
wrapped rows and entries; dragging to the pane's edge scrolls through retained
logs. Application selection is linear, not rectangular. Clicking controls,
dragging scrollbars, and double/triple-click selection are not supported.

- **Ctrl+C with a selection** requests a copy, not host cancellation.
- **Ctrl+C without a selection** stops the host.
- **Esc** clears a selection without stopping the host.
- **`q`** is an explicit dashboard quit command. While entering a function
  search, ordinary characters such as `q` remain query text.

Copied text is logical log text: real newlines and selected timestamp/source
text are preserved; ANSI styles, soft-wrap newlines, and continuation padding
are omitted. Hidden entries do not appear in a filtered selection.

A confirmed successful copy clears that selection. Failed or unconfirmed
copies keep it. If selected entries expire from history, the selection expires
with a notice instead of moving onto different text. A pending copy reports
expiry rather than stopping the host; use Esc to dismiss the expired selection.

Clipboard access uses native OS facilities, optional existing Linux utilities,
or a terminal-mediated OSC 52 request. No additional clipboard package or
utility is required to run the TUI. Terminal, remote-session, and multiplexer
permissions can prevent copying:

- Native/backend-confirmed writes report success.
- OSC 52 writes report a request sent to the terminal, **not confirmed
  clipboard success**. The protocol does not acknowledge the write.
- Unsupported access, helper failures, and oversized transport payloads are
  reported without silently truncating the copied selection.

Local clipboard writes are limited to 1 MiB of UTF-8 text; terminal-mediated
OSC 52 requests are limited to 64 KiB. These copy limits are independent of
the dashboard's retained-history budget.

The CLI never reads the clipboard, changes terminal permissions, or copies
automatically when selecting. Terminal-native shortcuts such as macOS Cmd+C
may be intercepted by the terminal and do not necessarily operate on
application-owned selections.

### Advanced: terminal selection

Terminal selection is an advanced fallback listed in `?` help, not the normal
shortcut footer. Use it to select other visible dashboard content or when
application clipboard access fails or is unconfirmed.

Press **`m`** to release mouse capture, clear the application-owned selection,
and pause automatic screen updates. The divider shows **Terminal selection**
and that the host is still running. Log collection continues within its usual
retention limits. Keyboard actions and resizing can still redraw the screen.

Select text with the terminal, then use **the terminal's Copy command**.
Pressing `m` does not itself copy anything. Ctrl+C forwarded to the CLI stops
the host in this mode; it is not the application-selection copy gesture.
The terminal copies rendered text, so wrapping and padding depend on the
terminal rather than the CLI's logical log representation.

Press `m` again to return to TUI mouse handling and redraws at the paused reading
position; `End` returns to live following. Some terminals also support a native
bypass gesture such as Shift-drag; the modifier varies by terminal.

Keyboard navigation remains available when the terminal does not report mouse
events. Use `--no-tui` for ordinary terminal scrollback and accessibility tools
that work better with streaming text.

### Shutdown

On completion, the CLI restores the previous screen and terminal modes, then
prints a concise summary of duration, invocations, and errors. A normal quit
does not replay earlier invocation errors from the dashboard. An actual nonzero
host exit still reports the exit code and the last logged error when available;
startup, terminal, and cleanup failures are not suppressed. During graceful
shutdown, another Ctrl+C requests force exit.

On Unix, Ctrl+Z suspends the foreground job after restoring terminal state.
Use `fg` to resume the dashboard; the owned input and display modes are reapplied.

Uncatchable termination, such as SIGKILL or externally terminating the process,
cannot guarantee terminal restoration. An unresponsive output transport can
also prevent visible restoration; input restoration is attempted first and
shutdown waits remain bounded. Mouse/clipboard support through SSH,
tmux, and individual terminal versions depends on their configuration.

## `plain`

Plain mode writes initialization status and streaming human-readable host
output without full-screen input, mouse capture, or live redraws. Use it for
CI, redirected output, normal shell scrollback, and text-oriented tooling.

## `json`

JSON mode emits newline-delimited JSON, with structured initialization records
before host events and a terminal `summary` record. It does not enter full-screen
mode or emit mouse/clipboard control sequences.

The schema remains documented in
[`func-start-json-schema.md`](func-start-json-schema.md).
