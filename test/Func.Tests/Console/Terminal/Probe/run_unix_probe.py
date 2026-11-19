#!/usr/bin/env python3
"""Qualify the product's Unix terminal adapter in a private PTY, never the user's terminal."""

import argparse
import errno
import fcntl
import hashlib
import json
import os
import platform
import pty
import re
import select
import signal
import struct
import subprocess
import sys
import termios
import time


def command_for(probe):
    return ["dotnet", probe] if probe.endswith(".dll") else [probe]


def same_terminal_configuration(actual, expected):
    if platform.system() == "Darwin":
        # The kernel sets PENDIN when ICANON is restored; do not flush pending input to clear it.
        actual, expected = list(actual), list(expected)
        actual[3] &= ~termios.PENDIN
        expected[3] &= ~termios.PENDIN
    return actual == expected


def session_child(probe, release_fd):
    # The supervisor keeps the probe group non-orphaned so SIGTSTP really stops it.
    print("INHERITED_SIGTSTP:" + str(signal.getsignal(signal.SIGTSTP)), flush=True)
    signal.signal(signal.SIGTTOU, signal.SIG_IGN)
    environment = os.environ.copy()
    environment["FUNC_TERMINAL_PROBE_SESSION"] = str(os.getsid(0))

    def foreground():
        os.setpgid(0, 0)
        os.tcsetpgrp(0, os.getpid())
        # Match an interactive shell; CI may ignore or block job-control signals.
        signals = {signal.SIGINT, signal.SIGQUIT, signal.SIGTSTP,
                   signal.SIGTTIN, signal.SIGTTOU, signal.SIGCONT}
        for number in signals:
            signal.signal(number, signal.SIG_DFL)
        signal.pthread_sigmask(signal.SIG_UNBLOCK, signals)

    child = subprocess.Popen(command_for(probe), env=environment, preexec_fn=foreground)
    print("PROBE_PID:" + str(child.pid), flush=True)
    while True:
        _, status = os.waitpid(child.pid, os.WUNTRACED)
        if os.WIFSTOPPED(status):
            print("PTY_STOPPED:" + str(os.WSTOPSIG(status)), flush=True)
        elif os.WIFEXITED(status):
            exit_code = os.WEXITSTATUS(status)
            break
        elif os.WIFSIGNALED(status):
            exit_code = 128 + os.WTERMSIG(status)
            break
    # Darwin invalidates the slave when its session leader exits.
    print("PROBE_EXIT:" + str(exit_code), flush=True)
    os.read(release_fd, 1)
    os.close(release_fd)
    return exit_code


def run(probe, report=None):
    master, slave = pty.openpty()
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 12, 80, 0, 0))
    original = termios.tcgetattr(slave)
    transcript = bytearray()
    process = None
    probe_pid = None
    release_read, release_write = os.pipe()
    deadline = time.monotonic() + 60

    def new_session():
        os.setsid()
        fcntl.ioctl(0, termios.TIOCSCTTY, 0)

    def wait_for(marker, complete_line=False, start=0):
        nonlocal transcript
        wanted = re.compile(re.escape(marker.encode()) + (rb"[^\r\n]*\r?\n" if complete_line else b""))
        while not wanted.search(transcript, start):
            if time.monotonic() >= deadline:
                raise RuntimeError("Timed out waiting for " + marker)
            if select.select([master], [], [], 0.1)[0]:
                try:
                    data = os.read(master, 65536)
                except OSError as error:
                    if error.errno == errno.EIO:
                        raise RuntimeError("PTY closed before " + marker) from error
                    raise
                if not data:
                    raise RuntimeError("PTY reached EOF before " + marker)
                transcript.extend(data)
                if b"PROBE_EXIT:" in transcript and not wanted.search(transcript, start):
                    raise RuntimeError("Probe exited before " + marker)
            elif process.poll() is not None:
                raise RuntimeError("Supervisor exited before " + marker)

    try:
        process = subprocess.Popen(
            [sys.executable, os.path.abspath(__file__), "--session-child", probe,
             "--release-fd", str(release_read)],
            stdin=slave, stdout=slave, stderr=slave, preexec_fn=new_session, pass_fds=(release_read,),
        )
        os.close(release_read)
        release_read = None
        wait_for("PROBE_PID:", complete_line=True)
        probe_pid = int(re.search(rb"PROBE_PID:(\d+)", transcript).group(1))
        wait_for("SIGNAL_READY")
        os.kill(probe_pid, signal.SIGTSTP)
        wait_for("SIGNAL_PROVEN")
        wait_for("INPUT_READY")
        os.write(master, (
            "\x1b[<64;5;3M\x1b[<0;5;3M\x1b[<32;6;4M\x1b[<0;6;4m"
            "😀\x03\x1b[200~cq\x03\x1b[201~"
        ).encode())
        wait_for("EVENTS_READY")

        fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 14, 96, 0, 0))
        wait_for("RESIZE_READY")
        for scope, send in (("process", os.kill), ("group", os.killpg)):
            print("SIGNAL_SCOPE:" + scope, file=sys.stderr)
            start = len(transcript)
            send(probe_pid, signal.SIGTSTP)
            wait_for("PTY_STOPPED:", start=start)
            suspended = termios.tcgetattr(slave)
            if not same_terminal_configuration(suspended, original):
                raise RuntimeError(f"Terminal input was not restored during {scope} suspension")
            if b"\x1b[?1049l" not in transcript[start:]:
                raise RuntimeError(f"{scope} suspension did not leave the alternate screen")

            send(probe_pid, signal.SIGCONT)
            os.write(master, b"c")
            wait_for("RESUME_READY:True", start=start)
        os.write(master, b"q")
        wait_for("RESULT ", complete_line=True)
        wait_for("PROBE_EXIT:", complete_line=True)
        final_attributes = termios.tcgetattr(slave)
        restored = same_terminal_configuration(final_attributes, original)
        os.write(release_write, b"1")
        os.close(release_write)
        release_write = None
        exit_code = process.wait(timeout=max(1, deadline - time.monotonic()))
        result_line = transcript.decode(errors="replace").split("RESULT ")[-1].splitlines()[0]
        if exit_code != 0 or not restored or "False" in result_line:
            if not restored:
                print(f"Original attributes: {original!r}\nFinal attributes: {final_attributes!r}", file=sys.stderr)
            raise RuntimeError(f"Native qualification failed: exit={exit_code} restored={restored} {result_line}")
        runtime = re.search(rb"RUNTIME (\S+) (\S+)\r?\n", transcript)
        if runtime is None:
            raise RuntimeError("Probe did not report its process architecture and runtime")
        with open(probe, "rb") as binary:
            probe_hash = hashlib.sha256(binary.read()).hexdigest()
        result = {
            "status": "passed",
            "os": platform.system(),
            "machine": platform.machine(),
            "process_architecture": runtime.group(1).decode("ascii"),
            "dotnet_runtime": runtime.group(2).decode("ascii"),
            "kernel": platform.release(),
            "probe": os.path.abspath(probe),
            "probe_sha256": probe_hash,
            "suspend_restored": True,
            "terminal_restored": restored,
            "result": result_line,
        }
        write_report(result, report)
        return 0
    except (OSError, RuntimeError, subprocess.TimeoutExpired, termios.error) as error:
        print(str(error), file=sys.stderr)
        print(transcript.decode(errors="replace"), file=sys.stderr)
        write_report({
            "status": "failed", "os": platform.system(), "machine": platform.machine(),
            "error": str(error), "transcript": transcript.decode(errors="replace"),
        }, report)
        return 1
    finally:
        if process is not None and process.poll() is None:
            if probe_pid is not None:
                try:
                    os.killpg(probe_pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            process.kill()
            process.wait()
        os.close(master)
        os.close(slave)
        for descriptor in (release_read, release_write):
            if descriptor is not None:
                os.close(descriptor)


def write_report(result, path):
    encoded = json.dumps(result, indent=2)
    print(encoded)
    if path:
        with open(path, "w", encoding="utf-8") as output:
            output.write(encoded + "\n")


def build_probe():
    folder = os.path.dirname(os.path.abspath(__file__))
    root = folder
    while not os.path.isfile(os.path.join(root, "global.json")):
        parent = os.path.dirname(root)
        if parent == root:
            raise RuntimeError("Could not find the source checkout")
        root = parent
    subprocess.run(
        ["dotnet", "build", os.path.join(root, "src", "Func", "Func.csproj"),
         "-c", "Release", "--verbosity", "quiet"],
        cwd=root, check=True,
    )
    spectre = os.path.join(root, "out", "bin", "Func", "release", "Spectre.Console.dll")
    subprocess.run(
        ["dotnet", "build", os.path.join(folder, "Terminal.Native.Probe.csproj"),
         "-c", "Release", "--verbosity", "quiet", "-p:CI=true",
         "-p:SpectreAssemblyPath=" + spectre],
        cwd=root, check=True,
    )
    return os.path.join(root, "out", "bin", "Terminal.Native.Probe", "release",
                        "Azure.Functions.Cli.Tests.TerminalProbe.dll")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("probe", nargs="?", help="Built native probe DLL or executable")
    parser.add_argument("--build", action="store_true", help="Build the CLI and native probe from this checkout first")
    parser.add_argument("--report", help="Write a machine-readable qualification result to this file")
    parser.add_argument("--session-child", metavar="PROBE", help=argparse.SUPPRESS)
    parser.add_argument("--release-fd", type=int, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.session_child:
        if args.release_fd is None:
            parser.error("--session-child requires a private release pipe")
        sys.exit(session_child(args.session_child, args.release_fd))
    if args.build:
        args.probe = build_probe()
    if not args.probe:
        parser.error("a built probe DLL/executable or --build is required")
    sys.exit(run(os.path.abspath(args.probe), args.report))
