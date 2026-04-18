#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# test-workload-local.sh
#
# Builds the CLI and dotnet workload, installs the workload locally, and
# optionally runs a quick e2e smoke test (func init + func new).
#
# Usage:
#   ./eng/scripts/test-workload-local.sh            # build + install
#   ./eng/scripts/test-workload-local.sh --smoke     # build + install + smoke test
#   ./eng/scripts/test-workload-local.sh --clean     # remove local workload install
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKLOAD_VERSION="0.1.0-local"
WORKLOAD_ID="dotnet"
PACKAGE_ID="Azure.Functions.Cli.Workload.Dotnet"
ASSEMBLY_NAME="Azure.Functions.Cli.Workload.Dotnet.dll"
WORKLOADS_DIR="$HOME/.azure-functions/workloads"
INSTALL_DIR="$WORKLOADS_DIR/$WORKLOAD_ID/$WORKLOAD_VERSION"
MANIFEST_PATH="$WORKLOADS_DIR/workloads.json"

# Detect RID
case "$(uname -s)-$(uname -m)" in
    Darwin-arm64)  RID="osx-arm64" ;;
    Darwin-x86_64) RID="osx-x64" ;;
    Linux-x86_64)  RID="linux-x64" ;;
    Linux-aarch64) RID="linux-arm64" ;;
    *)             echo "Unsupported platform: $(uname -s)-$(uname -m)"; exit 1 ;;
esac

CLI_PUBLISH_DIR="$REPO_ROOT/out/pub/Func.Cli/release_$RID"
FUNC="$CLI_PUBLISH_DIR/func"

# ---- Clean mode -----------------------------------------------------------
if [[ "${1:-}" == "--clean" ]]; then
    echo "🧹 Removing local workload install..."
    rm -rf "$INSTALL_DIR"
    # Remove from manifest if it exists
    if [[ -f "$MANIFEST_PATH" ]]; then
        # Remove workload entry; if that leaves an empty list, remove manifest
        if command -v python3 &>/dev/null; then
            python3 -c "
import json, sys
with open('$MANIFEST_PATH') as f:
    m = json.load(f)
m['workloads'] = [w for w in m['workloads'] if w['id'] != '$WORKLOAD_ID']
with open('$MANIFEST_PATH', 'w') as f:
    json.dump(m, f, indent=2)
"
            echo "✅ Workload '$WORKLOAD_ID' removed from manifest."
        else
            echo "⚠️  python3 not found — please remove '$WORKLOAD_ID' from $MANIFEST_PATH manually."
        fi
    fi
    exit 0
fi

# ---- Build ----------------------------------------------------------------
echo "📦 Publishing CLI ($RID)..."
dotnet publish "$REPO_ROOT/src/Func.Cli/Func.Cli.csproj" -c release -r "$RID" --no-self-contained -o "$CLI_PUBLISH_DIR" -v quiet

echo "🔨 Building dotnet workload..."
dotnet build "$REPO_ROOT/src/Func.Cli.Workload.Dotnet/Func.Cli.Workload.Dotnet.csproj" -c release -v quiet

# ---- Install workload locally ---------------------------------------------
WORKLOAD_BIN_DIR="$REPO_ROOT/out/bin/Func.Cli.Workload.Dotnet/release"

echo "📂 Installing workload to $INSTALL_DIR..."
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
cp "$WORKLOAD_BIN_DIR"/*.dll "$INSTALL_DIR/"
cp "$WORKLOAD_BIN_DIR"/*.deps.json "$INSTALL_DIR/" 2>/dev/null || true

# ---- Update manifest ------------------------------------------------------
mkdir -p "$WORKLOADS_DIR"

# Build JSON manifest entry using python3 for reliability
NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
python3 -c "
import json, os

manifest_path = '$MANIFEST_PATH'
entry = {
    'id': '$WORKLOAD_ID',
    'packageId': '$PACKAGE_ID',
    'version': '$WORKLOAD_VERSION',
    'installPath': '$INSTALL_DIR',
    'assemblyName': '$ASSEMBLY_NAME',
    'installedAt': '$NOW'
}

if os.path.exists(manifest_path):
    with open(manifest_path) as f:
        m = json.load(f)
else:
    m = {'schemaVersion': 1, 'workloads': []}

# Replace existing entry for this workload
m['workloads'] = [w for w in m['workloads'] if w['id'] != '$WORKLOAD_ID']
m['workloads'].append(entry)

with open(manifest_path, 'w') as f:
    json.dump(m, f, indent=2)
"

echo "✅ Workload installed and registered in manifest."
echo ""
echo "CLI:      $FUNC"
echo "Workload: $INSTALL_DIR"
echo ""

# ---- Smoke test -----------------------------------------------------------
if [[ "${1:-}" == "--smoke" ]]; then
    SMOKE_DIR=$(mktemp -d)
    echo "🧪 Running smoke test in $SMOKE_DIR..."

    cd "$SMOKE_DIR"
    echo "  → func init MyApp --worker-runtime dotnet"
    "$FUNC" init MyApp --worker-runtime dotnet

    cd MyApp
    echo "  → func new --template HttpTrigger --name MyFunc"
    "$FUNC" new --template HttpTrigger --name MyFunc

    echo ""
    echo "✅ Smoke test passed! Project created at $SMOKE_DIR/MyApp"
    echo "   Files:"
    find . -type f | sort | head -20
else
    echo "Tip: run with --smoke to do a quick e2e test"
fi
