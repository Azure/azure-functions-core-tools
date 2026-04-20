#!/usr/bin/env bash
# =============================================================================
# Azurite Integration Smoke Test
# =============================================================================
# Tests the Azurite auto-start flows in func start:
#   1. No local.settings.json → smart defaults kick in
#   2. Azurite via npx (when npx available)
#   3. Azurite via Docker (when only docker available)
#   4. No Azurite available → shows install instructions
#
# Usage:
#   ./test-azurite-flow.sh [--npx-only | --docker-only | --none-only | --all]
#
# Prerequisites:
#   - func v5 built (uses $FUNC_PATH or auto-detects from out/pub/)
#   - A .NET function app (uses _v5DotnetApp by default)
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GREY='\033[0;90m'
NC='\033[0m' # No Color

# Config
FUNC_PATH="${FUNC_PATH:-$REPO_ROOT/out/pub/Func.Cli/release_osx-arm64/func}"
APP_DIR="${APP_DIR:-$REPO_ROOT/_v5DotnetApp}"
TIMEOUT=30  # seconds to wait for host to start
HOST_PORT=17071  # use non-default port to avoid conflicts

# Track results
TESTS_RUN=0
TESTS_PASSED=0
TESTS_FAILED=0
CLEANUP_PIDS=()

# =============================================================================
# Helpers
# =============================================================================

log_header() {
    echo ""
    echo -e "${CYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${CYAN}  $1${NC}"
    echo -e "${CYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
}

log_step() {
    echo -e "${GREY}  → $1${NC}"
}

log_pass() {
    echo -e "${GREEN}  ✓ $1${NC}"
    ((TESTS_PASSED++))
    ((TESTS_RUN++))
}

log_fail() {
    echo -e "${RED}  ✗ $1${NC}"
    ((TESTS_FAILED++))
    ((TESTS_RUN++))
}

log_warn() {
    echo -e "${YELLOW}  ⚠ $1${NC}"
}

cleanup() {
    log_step "Cleaning up..."

    # Kill any func processes we started
    for pid in "${CLEANUP_PIDS[@]}"; do
        if kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
            log_step "Killed process $pid"
        fi
    done

    # Stop Azurite docker container if we started one
    if docker ps -q --filter "name=func-azurite" 2>/dev/null | grep -q .; then
        docker stop func-azurite >/dev/null 2>&1 || true
        docker rm func-azurite >/dev/null 2>&1 || true
        log_step "Stopped Docker Azurite container"
    fi

    # Kill any Azurite processes on our test port
    lsof -ti :10000 2>/dev/null | xargs kill 2>/dev/null || true

    # Restore local.settings.json if we backed it up
    if [[ -f "$APP_DIR/local.settings.json.bak" ]]; then
        mv "$APP_DIR/local.settings.json.bak" "$APP_DIR/local.settings.json"
        log_step "Restored local.settings.json"
    fi
}

trap cleanup EXIT

# Hide a command from PATH by creating a restricted PATH
path_without() {
    local cmd_to_hide="$1"
    local cmd_path
    cmd_path="$(command -v "$cmd_to_hide" 2>/dev/null || true)"
    if [[ -z "$cmd_path" ]]; then
        echo "$PATH"
        return
    fi
    local cmd_dir
    cmd_dir="$(dirname "$cmd_path")"
    echo "$PATH" | tr ':' '\n' | grep -v "^${cmd_dir}$" | tr '\n' ':' | sed 's/:$//'
}

# Start func start in background, capture output, wait for host ready or timeout
# Args: $1=label, $2=extra env (optional PATH override)
# Returns: 0 if host started, 1 if timeout/error
# Sets: FUNC_OUTPUT (captured output), FUNC_PID
start_func() {
    local label="$1"
    local extra_env="${2:-}"
    local output_file
    output_file=$(mktemp)

    log_step "Starting func start ($label)..."

    # Build env command
    local env_cmd=""
    if [[ -n "$extra_env" ]]; then
        env_cmd="env $extra_env"
    fi

    # Run func start with --no-build (we pre-build once) on a non-default port
    $env_cmd "$FUNC_PATH" start --no-build --port "$HOST_PORT" \
        > "$output_file" 2>&1 &
    FUNC_PID=$!
    CLEANUP_PIDS+=("$FUNC_PID")

    # Wait for host to be ready or timeout
    local elapsed=0
    while [[ $elapsed -lt $TIMEOUT ]]; do
        if ! kill -0 "$FUNC_PID" 2>/dev/null; then
            # Process exited
            break
        fi

        # Check if host is listening
        if curl -s -o /dev/null -w "%{http_code}" "http://localhost:$HOST_PORT/" 2>/dev/null | grep -q "200\|404"; then
            sleep 1  # Give it a moment to finish startup output
            break
        fi

        sleep 1
        ((elapsed++))
    done

    FUNC_OUTPUT=$(cat "$output_file")
    rm -f "$output_file"

    # Kill the func process (we just needed to check startup behavior)
    if kill -0 "$FUNC_PID" 2>/dev/null; then
        kill "$FUNC_PID" 2>/dev/null || true
        wait "$FUNC_PID" 2>/dev/null || true
    fi
}

# =============================================================================
# Pre-checks
# =============================================================================

log_header "Pre-flight checks"

if [[ ! -f "$FUNC_PATH" ]]; then
    echo -e "${RED}ERROR: func binary not found at $FUNC_PATH${NC}"
    echo "Set FUNC_PATH or build with: dotnet publish src/Func.Cli -c Release -r osx-arm64"
    exit 1
fi
log_pass "func binary found: $FUNC_PATH"

if [[ ! -d "$APP_DIR" ]]; then
    echo -e "${RED}ERROR: Function app not found at $APP_DIR${NC}"
    echo "Set APP_DIR to a .NET function app directory"
    exit 1
fi
log_pass "Function app found: $APP_DIR"

# Pre-build the app once
log_step "Pre-building function app..."
(cd "$APP_DIR" && dotnet build -v q 2>&1 | tail -3)
log_pass "App built"

# Back up local.settings.json if it exists
if [[ -f "$APP_DIR/local.settings.json" ]]; then
    cp "$APP_DIR/local.settings.json" "$APP_DIR/local.settings.json.bak"
    rm "$APP_DIR/local.settings.json"
    log_step "Backed up and removed local.settings.json"
fi

# Kill any existing Azurite
lsof -ti :10000 2>/dev/null | xargs kill 2>/dev/null || true
docker stop func-azurite 2>/dev/null || true
docker rm func-azurite 2>/dev/null || true
sleep 1

# =============================================================================
# Test 1: Smart defaults without local.settings.json
# =============================================================================

test_smart_defaults() {
    log_header "Test 1: Smart defaults (no local.settings.json)"

    # Pre-start Azurite so the prompt doesn't block
    log_step "Pre-starting Azurite for clean test..."
    npx azurite --silent --location /tmp/azurite-test &
    local azurite_pid=$!
    CLEANUP_PIDS+=("$azurite_pid")
    sleep 3

    cd "$APP_DIR"
    start_func "smart-defaults"

    echo -e "${GREY}  --- Output ---${NC}"
    echo "$FUNC_OUTPUT" | head -20 | sed 's/^/  │ /'
    echo -e "${GREY}  --- End ---${NC}"

    # Check: should NOT complain about missing local.settings.json
    if echo "$FUNC_OUTPUT" | grep -qi "local.settings.json.*not found\|could not find.*local.settings"; then
        log_fail "Complained about missing local.settings.json"
    else
        log_pass "No complaint about missing local.settings.json"
    fi

    # Check: should auto-detect dotnet-isolated
    if echo "$FUNC_OUTPUT" | grep -qi "FUNCTIONS_WORKER_RUNTIME.*required"; then
        log_fail "Worker runtime not auto-detected (host complained)"
    else
        log_pass "Worker runtime auto-detected (no host complaint)"
    fi

    # Check: should show host startup
    if echo "$FUNC_OUTPUT" | grep -qi "Host version\|Host process started"; then
        log_pass "Host started successfully"
    else
        log_fail "Host did not start"
    fi

    # Cleanup Azurite
    kill "$azurite_pid" 2>/dev/null || true
    wait "$azurite_pid" 2>/dev/null || true
    sleep 1
}

# =============================================================================
# Test 2: Azurite auto-start via npx
# =============================================================================

test_npx_flow() {
    log_header "Test 2: Azurite auto-start via npx"

    if ! command -v npx &>/dev/null; then
        log_warn "npx not available — skipping"
        return
    fi

    # Ensure Azurite is NOT running
    lsof -ti :10000 2>/dev/null | xargs kill 2>/dev/null || true
    sleep 1

    log_step "Azurite is not running on port 10000"

    # We can't easily interact with the confirm prompt in a script,
    # so we test that the detection message appears
    cd "$APP_DIR"

    local output_file
    output_file=$(mktemp)

    # Start func, auto-answer 'y' to the Azurite prompt via yes pipe
    yes y 2>/dev/null | timeout "$TIMEOUT" "$FUNC_PATH" start --no-build --port "$HOST_PORT" \
        > "$output_file" 2>&1 &
    local func_pid=$!
    CLEANUP_PIDS+=("$func_pid")

    # Wait for output
    sleep 10

    FUNC_OUTPUT=$(cat "$output_file")
    rm -f "$output_file"

    echo -e "${GREY}  --- Output ---${NC}"
    echo "$FUNC_OUTPUT" | head -25 | sed 's/^/  │ /'
    echo -e "${GREY}  --- End ---${NC}"

    # Check: should detect Azurite is not running
    if echo "$FUNC_OUTPUT" | grep -qi "Azurite is not running\|UseDevelopmentStorage.*Azurite"; then
        log_pass "Detected Azurite not running"
    else
        log_fail "Did not detect missing Azurite"
    fi

    # Check: should mention npx
    if echo "$FUNC_OUTPUT" | grep -qi "npx azurite\|Start Azurite"; then
        log_pass "Offered npx azurite option"
    else
        log_fail "Did not offer npx option"
    fi

    # Check: should show PID if started
    if echo "$FUNC_OUTPUT" | grep -qi "PID:"; then
        log_pass "Showed Azurite PID"
    else
        log_warn "PID not shown (prompt may not have been answered)"
    fi

    # Cleanup
    kill "$func_pid" 2>/dev/null || true
    wait "$func_pid" 2>/dev/null || true
    lsof -ti :10000 2>/dev/null | xargs kill 2>/dev/null || true
    sleep 1
}

# =============================================================================
# Test 3: Azurite auto-start via Docker (npx hidden)
# =============================================================================

test_docker_flow() {
    log_header "Test 3: Azurite auto-start via Docker (npx hidden)"

    if ! command -v docker &>/dev/null; then
        log_warn "Docker not available — skipping"
        return
    fi

    # Ensure Azurite is NOT running
    lsof -ti :10000 2>/dev/null | xargs kill 2>/dev/null || true
    docker stop func-azurite 2>/dev/null || true
    docker rm func-azurite 2>/dev/null || true
    sleep 1

    # Create a PATH that hides npx and node
    local restricted_path
    restricted_path=$(path_without npx)

    log_step "PATH restricted: npx hidden"
    log_step "Docker available: $(command -v docker)"

    cd "$APP_DIR"

    local output_file
    output_file=$(mktemp)

    # Start func with npx hidden, auto-answer 'y'
    yes y 2>/dev/null | timeout "$TIMEOUT" env "PATH=$restricted_path" \
        "$FUNC_PATH" start --no-build --port "$HOST_PORT" \
        > "$output_file" 2>&1 &
    local func_pid=$!
    CLEANUP_PIDS+=("$func_pid")

    sleep 12

    FUNC_OUTPUT=$(cat "$output_file")
    rm -f "$output_file"

    echo -e "${GREY}  --- Output ---${NC}"
    echo "$FUNC_OUTPUT" | head -25 | sed 's/^/  │ /'
    echo -e "${GREY}  --- End ---${NC}"

    # Check: should offer Docker
    if echo "$FUNC_OUTPUT" | grep -qi "docker\|Docker"; then
        log_pass "Offered Docker option"
    else
        log_fail "Did not offer Docker option"
    fi

    # Check: should mention container name
    if echo "$FUNC_OUTPUT" | grep -qi "func-azurite"; then
        log_pass "Showed container name func-azurite"
    else
        log_warn "Container name not shown (may not have started)"
    fi

    # Cleanup
    kill "$func_pid" 2>/dev/null || true
    wait "$func_pid" 2>/dev/null || true
    docker stop func-azurite 2>/dev/null || true
    docker rm func-azurite 2>/dev/null || true
    sleep 1
}

# =============================================================================
# Test 4: No Azurite available (both hidden)
# =============================================================================

test_no_azurite() {
    log_header "Test 4: No Azurite available (npx + docker hidden)"

    # Ensure Azurite is NOT running
    lsof -ti :10000 2>/dev/null | xargs kill 2>/dev/null || true
    sleep 1

    # Hide both npx and docker
    local restricted_path
    restricted_path=$(path_without npx)
    restricted_path=$(echo "$restricted_path" | tr ':' '\n' | grep -v docker | tr '\n' ':' | sed 's/:$//')

    log_step "PATH restricted: npx and docker hidden"

    cd "$APP_DIR"

    local output_file
    output_file=$(mktemp)

    timeout "$TIMEOUT" env "PATH=$restricted_path" \
        "$FUNC_PATH" start --no-build --port "$HOST_PORT" \
        > "$output_file" 2>&1 &
    local func_pid=$!
    CLEANUP_PIDS+=("$func_pid")

    sleep 8

    FUNC_OUTPUT=$(cat "$output_file")
    rm -f "$output_file"

    echo -e "${GREY}  --- Output ---${NC}"
    echo "$FUNC_OUTPUT" | head -25 | sed 's/^/  │ /'
    echo -e "${GREY}  --- End ---${NC}"

    # Check: should show install instructions
    if echo "$FUNC_OUTPUT" | grep -qi "npm install.*azurite\|docker pull"; then
        log_pass "Showed Azurite install instructions"
    else
        log_fail "Did not show install instructions"
    fi

    # Check: should mention continuing without
    if echo "$FUNC_OUTPUT" | grep -qi "Continuing without\|storage.*trigger.*may fail"; then
        log_pass "Warned about continuing without Azurite"
    else
        log_warn "No 'continuing without' warning (may have exited)"
    fi

    # Cleanup
    kill "$func_pid" 2>/dev/null || true
    wait "$func_pid" 2>/dev/null || true
}

# =============================================================================
# Test 5: Ctrl+C message placement
# =============================================================================

test_ctrlc_placement() {
    log_header "Test 5: Ctrl+C message appears after PID"

    # Pre-start Azurite so prompt doesn't block
    npx azurite --silent --location /tmp/azurite-test 2>/dev/null &
    local azurite_pid=$!
    CLEANUP_PIDS+=("$azurite_pid")
    sleep 3

    cd "$APP_DIR"
    start_func "ctrlc-placement"

    echo -e "${GREY}  --- Output ---${NC}"
    echo "$FUNC_OUTPUT" | head -20 | sed 's/^/  │ /'
    echo -e "${GREY}  --- End ---${NC}"

    # Check: "Host process started (PID:" should appear
    if echo "$FUNC_OUTPUT" | grep -qi "Host process started.*PID"; then
        log_pass "PID line present"
    else
        log_fail "PID line missing"
    fi

    # Check: "Press Ctrl+C" should appear right after PID line
    local pid_line_num ctrl_line_num
    pid_line_num=$(echo "$FUNC_OUTPUT" | grep -n "Host process started" | head -1 | cut -d: -f1)
    ctrl_line_num=$(echo "$FUNC_OUTPUT" | grep -n "Ctrl+C" | head -1 | cut -d: -f1)

    if [[ -n "$pid_line_num" && -n "$ctrl_line_num" ]]; then
        local diff=$((ctrl_line_num - pid_line_num))
        if [[ $diff -le 2 ]]; then
            log_pass "Ctrl+C message is right after PID (line $pid_line_num → $ctrl_line_num)"
        else
            log_fail "Ctrl+C message too far from PID ($diff lines apart)"
        fi
    else
        log_warn "Could not determine line positions"
    fi

    # Cleanup
    kill "$azurite_pid" 2>/dev/null || true
    wait "$azurite_pid" 2>/dev/null || true
    sleep 1
}

# =============================================================================
# Main
# =============================================================================

MODE="${1:---all}"

case "$MODE" in
    --npx-only)
        test_npx_flow
        ;;
    --docker-only)
        test_docker_flow
        ;;
    --none-only)
        test_no_azurite
        ;;
    --smart-defaults)
        test_smart_defaults
        ;;
    --ctrlc)
        test_ctrlc_placement
        ;;
    --all)
        test_smart_defaults
        test_npx_flow
        test_docker_flow
        test_no_azurite
        test_ctrlc_placement
        ;;
    *)
        echo "Usage: $0 [--npx-only | --docker-only | --none-only | --smart-defaults | --ctrlc | --all]"
        exit 1
        ;;
esac

# =============================================================================
# Summary
# =============================================================================

log_header "Results"
echo -e "  Tests run:    $TESTS_RUN"
echo -e "  ${GREEN}Passed:      $TESTS_PASSED${NC}"
if [[ $TESTS_FAILED -gt 0 ]]; then
    echo -e "  ${RED}Failed:      $TESTS_FAILED${NC}"
fi
echo ""

exit "$TESTS_FAILED"
