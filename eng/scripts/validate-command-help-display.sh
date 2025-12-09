#!/bin/bash

# Test script to verify all --help commands work without exceptions
# This ensures no breaking changes were introduced to the help system

# Usage: ./eng/scripts/validate-command-help-display.sh

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Detect repo root - check if we're in repo root or eng/scripts
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "$SCRIPT_DIR/../../global.json" ]; then
    # We're in eng/scripts
    REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
else
    # Assume we're in repo root
    REPO_ROOT="$(pwd)"
fi

echo -e "${YELLOW}Repository root: $REPO_ROOT${NC}"

# Validate we found the right directory
if [ ! -f "$REPO_ROOT/global.json" ]; then
    echo -e "${RED}Error: Could not find repository root. Please run from repo root or eng/scripts${NC}"
    exit 1
fi

# Build the project first
echo -e "${YELLOW}Building project...${NC}"
if ! dotnet build "$REPO_ROOT/src/Cli/func/Azure.Functions.Cli.csproj" > /dev/null 2>&1; then
    echo -e "${RED}Build failed!${NC}"
    exit 1
fi
echo -e "${GREEN}Build successful${NC}\n"

# Path to the built executable
FUNC_CMD="dotnet run --project $REPO_ROOT/src/Cli/func/Azure.Functions.Cli.csproj --no-build --"

# Create output directory for help command outputs
OUTPUT_DIR="$REPO_ROOT/out/help-command-outputs"
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Counter for test results
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0
FAILED_COMMANDS=()

# Function to test a command
test_command() {
    local cmd="$1"
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
    # Create a sanitized filename from the command
    local filename=$(echo "$cmd" | sed 's/ /_/g' | sed 's/--//g').txt
    local output_file="$OUTPUT_DIR/$filename"
    
    echo -n "Testing: $cmd ... "
    
    # Run the command and capture output and exit code
    set +e  # Temporarily disable exit on error
    output=$($FUNC_CMD $cmd 2>&1)
    exit_code=$?
    set -e  # Re-enable exit on error
    
    # Write output to file
    echo "Command: func $cmd" > "$output_file"
    echo "Exit code: $exit_code" >> "$output_file"
    echo "======================================" >> "$output_file"
    echo "$output" >> "$output_file"
    
    # Check for exceptions/errors in output (but ignore "Error: unknown argument" which is expected for some cases)
    if echo "$output" | grep -qi "exception"; then
        echo -e "${RED}FAILED (Exception found)${NC}"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        FAILED_COMMANDS+=("$cmd")
        echo "See: $output_file"
        return 0  # Return 0 to not exit the script
    elif echo "$output" | grep -qi "error:" | grep -qv "Error: unknown argument"; then
        echo -e "${RED}FAILED (Error found)${NC}"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        FAILED_COMMANDS+=("$cmd")
        echo "See: $output_file"
        return 0  # Return 0 to not exit the script
    elif [ $exit_code -ne 0 ] && [ $exit_code -ne 1 ]; then
        # Exit code 1 is OK for help commands (they exit after displaying help)
        echo -e "${RED}FAILED (Exit code: $exit_code)${NC}"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        FAILED_COMMANDS+=("$cmd")
        echo "See: $output_file"
        return 0  # Return 0 to not exit the script
    # Check if help output is missing expected help indicators
    elif ! echo "$output" | grep -qi "usage:\|actions:\|options:\|contexts:"; then
        echo -e "${RED}FAILED (No help content found)${NC}"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        FAILED_COMMANDS+=("$cmd")
        echo "See: $output_file"
        return 0  # Return 0 to not exit the script
    else
        echo -e "${GREEN}PASSED${NC}"
        PASSED_TESTS=$((PASSED_TESTS + 1))
        return 0
    fi
}

echo -e "${YELLOW}Testing help commands...${NC}\n"

# Test root level help
test_command "--help"
test_command "help"

# Test contexts that should still work (even if hidden from help)
echo -e "\n${YELLOW}Testing context help commands...${NC}"
test_command "azure --help"
test_command "azurecontainerapps --help"
test_command "durable --help"
test_command "extensions --help"
test_command "host --help"
test_command "function --help"
test_command "kubernetes --help"
test_command "settings --help"
test_command "templates --help"

# Test subcontext help
echo -e "\n${YELLOW}Testing subcontext help commands...${NC}"
test_command "azure functionapp --help"
test_command "azure storage --help"

# Test top-level actions
echo -e "\n${YELLOW}Testing top-level action help commands...${NC}"
test_command "init --help"
test_command "new --help"
test_command "pack --help"
test_command "start --help"
test_command "logs --help"

# Test azure actions
echo -e "\n${YELLOW}Testing azure action help commands...${NC}"
test_command "azure functionapp publish --help"
test_command "azure functionapp list-functions --help"
test_command "azure functionapp fetch-app-settings --help"
test_command "azure functionapp fetch --help"
test_command "azure functionapp logstream --help"
test_command "azure storage fetch-connection-string --help"

# Test kubernetes actions
echo -e "\n${YELLOW}Testing kubernetes action help commands...${NC}"
test_command "kubernetes install --help"
test_command "kubernetes remove --help"
test_command "kubernetes deploy --help"
test_command "kubernetes delete --help"
test_command "kubernetes logs --help"

# Test azurecontainerapps actions
echo -e "\n${YELLOW}Testing azurecontainerapps action help commands...${NC}"
test_command "azurecontainerapps deploy --help"

# Test durable actions
echo -e "\n${YELLOW}Testing durable action help commands...${NC}"
test_command "durable get-instances --help"
test_command "durable get-history --help"
test_command "durable get-runtime-status --help"
test_command "durable start-new --help"
test_command "durable raise-event --help"
test_command "durable terminate --help"
test_command "durable rewind --help"
test_command "durable purge-history --help"
test_command "durable delete-task-hub --help"

# Test extensions actions
echo -e "\n${YELLOW}Testing extensions action help commands...${NC}"
test_command "extensions install --help"
test_command "extensions sync --help"

# Test settings actions
echo -e "\n${YELLOW}Testing settings action help commands...${NC}"
test_command "settings add --help"
test_command "settings list --help"
test_command "settings delete --help"
test_command "settings remove --help"
test_command "settings encrypt --help"
test_command "settings decrypt --help"

# Test templates actions
echo -e "\n${YELLOW}Testing templates action help commands...${NC}"
test_command "templates list --help"

# Test host actions (should still work even if hidden)
echo -e "\n${YELLOW}Testing host action help commands...${NC}"
test_command "host start --help"

# Test function actions (should still work even if hidden)
echo -e "\n${YELLOW}Testing function action help commands...${NC}"
test_command "function new --help"
test_command "function create --help"

# Test init subcommands
echo -e "\n${YELLOW}Testing init subcommand help...${NC}"
test_command "init dotnet --help"
test_command "init node --help"
test_command "init python --help"
test_command "init powershell --help"

# Test pack subcommands
echo -e "\n${YELLOW}Testing pack subcommand help...${NC}"
test_command "pack node --help"
test_command "pack python --help"

# Print summary
echo -e "\n${YELLOW}===============================================${NC}"
echo -e "${YELLOW}Test Summary${NC}"
echo -e "${YELLOW}===============================================${NC}"
echo -e "Total tests: $TOTAL_TESTS"
echo -e "${GREEN}Passed: $PASSED_TESTS${NC}"
echo -e "${RED}Failed: $FAILED_TESTS${NC}"
echo -e "\n${YELLOW}Output files saved to: $OUTPUT_DIR/${NC}"

if [ $FAILED_TESTS -gt 0 ]; then
    echo -e "\n${RED}Failed commands:${NC}"
    for cmd in "${FAILED_COMMANDS[@]}"; do
        echo -e "  - $cmd"
    done
    exit 1
else
    echo -e "\n${GREEN}All help commands work correctly!${NC}"
    exit 0
fi
