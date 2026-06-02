#!/usr/bin/env bash
set -euo pipefail

# Azure Functions Core Tools CLI installer
# Usage: curl -sSL https://aka.ms/func-cli/install.sh | bash

REPO="${SOURCE:-Azure/azure-functions-core-tools}"
API_BASE="https://api.github.com/repos/${REPO}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.azure-functions}"
VERSION="${VERSION:-}"
PRERELEASE="${PRERELEASE:-false}"
FORCE="${FORCE:-false}"
BUGBASH="${BUGBASH:-false}"

# --- Parse flags ---

for arg in "$@"; do
    case "$arg" in
        --bugbash) BUGBASH="true" ;;
        --prerelease) PRERELEASE="true" ;;
        --force) FORCE="true" ;;
    esac
done

BUGBASH_WORKLOADS_SOURCE="https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json"
BUGBASH_QUICKSTART_MANIFEST_URL="https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json"

# --- Detect platform ---

case "$(uname -s)" in
    Linux*)  OS="linux" ;;
    Darwin*) OS="osx" ;;
    *)       echo "Error: Unsupported OS: $(uname -s)" >&2; exit 1 ;;
esac

case "$(uname -m)" in
    x86_64|amd64)  ARCH="x64" ;;
    arm64|aarch64) ARCH="arm64" ;;
    *)             echo "Error: Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

ASSET_NAME="func-${OS}-${ARCH}.tar.gz"

# --- Resolve version ---

if [ -z "$VERSION" ]; then
    if [ "$PRERELEASE" = "true" ]; then
        echo "Resolving latest 5.x pre-release..."
    else
        echo "Resolving latest stable 5.x release..."
    fi

    # GitHub API returns releases newest-first. Extract tag_name + prerelease pairs, then filter.
    RELEASES_JSON=$(curl -sSL "${API_BASE}/releases?per_page=50")
    VERSION=$(echo "$RELEASES_JSON" \
        | tr ',' '\n' \
        | grep -E '"tag_name"|"prerelease"' \
        | sed -E 's/.*"tag_name"[[:space:]]*:[[:space:]]*"([^"]*)".*/tag:\1/; s/.*"prerelease"[[:space:]]*:[[:space:]]*([^[:space:]]+).*/pre:\1/' \
        | paste - - \
        | awk -F'\t' -v include_pre="$PRERELEASE" '
            {
                sub(/^tag:/, "", $1); tag = $1
                sub(/^pre:/, "", $2); pre = $2
                if (tag ~ /^v?5\./ && (include_pre == "true" || pre == "false")) {
                    print tag; exit
                }
            }')

    if [ -z "$VERSION" ]; then
        if [ "$PRERELEASE" != "true" ]; then
            PRE_VERSIONS=$(echo "$RELEASES_JSON" \
                | tr ',' '\n' \
                | grep -E '"tag_name"|"prerelease"' \
                | sed -E 's/.*"tag_name"[[:space:]]*:[[:space:]]*"([^"]*)".*/tag:\1/; s/.*"prerelease"[[:space:]]*:[[:space:]]*([^[:space:]]+).*/pre:\1/' \
                | paste - - \
                | awk -F'\t' '
                    {
                        sub(/^tag:/, "", $1); tag = $1
                        sub(/^pre:/, "", $2); pre = $2
                        if (tag ~ /^v?5\./ && pre == "true") { print tag; count++; if (count >= 5) exit }
                    }')

            if [ -n "$PRE_VERSIONS" ]; then
                echo -e "\033[31mNo stable 5.x release found. Available pre-releases:\033[0m" >&2
                echo "$PRE_VERSIONS" | sed 's/^/  /' | while IFS= read -r line; do echo -e "\033[31m${line}\033[0m"; done >&2
                echo "" >&2
                echo -e "\033[31mTo install a pre-release, re-run with PRERELEASE=true\033[0m" >&2
                exit 1
            fi
        fi
        echo "Error: Could not find a 5.x release."
        exit 1
    fi
fi

# Ensure version has 'v' prefix
case "$VERSION" in
    v*) ;;
    *)  VERSION="v${VERSION}" ;;
esac

echo "Installing func CLI ${VERSION} (${OS}-${ARCH})..."

# --- Check existing install ---

if [ -f "${INSTALL_DIR}/func" ] && [ "$FORCE" != "true" ]; then
    echo -e "\033[31mfunc CLI is already installed at ${INSTALL_DIR}.\033[0m"
    echo -e "\033[31mTo overwrite, re-run with FORCE=true.\033[0m"
    exit 0
fi

# --- Download and extract ---

DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${VERSION}/${ASSET_NAME}"
TEMP_DIR=$(mktemp -d)
trap 'rm -rf "$TEMP_DIR"' EXIT

curl -sSL -o "${TEMP_DIR}/${ASSET_NAME}" "$DOWNLOAD_URL"
mkdir -p "$INSTALL_DIR"
tar -xzf "${TEMP_DIR}/${ASSET_NAME}" -C "$INSTALL_DIR"

if [ "$OS" = "osx" ]; then
    xattr -d com.apple.quarantine "${INSTALL_DIR}/func" 2>/dev/null || true
fi

# Drop a func5 wrapper so v5 can be invoked side-by-side with a v4 `func` on PATH.
cat > "${INSTALL_DIR}/func5" <<'EOF'
#!/usr/bin/env bash
exec "$(dirname "$0")/func" "$@"
EOF
chmod +x "${INSTALL_DIR}/func5"

# --- Update PATH ---

# Detect a pre-existing 'func' that lives outside our install dir (e.g. Core Tools v4).
# If one is present we APPEND our dir so the existing 'func' keeps winning and only
# 'func5' resolves to v5. Otherwise we PREPEND so new users get 'func' = v5 by default.
EXISTING_FUNC=""
if command -v func >/dev/null 2>&1; then
    RESOLVED=$(command -v func)
    case "$RESOLVED" in
        "${INSTALL_DIR}/"*) ;;
        *) EXISTING_FUNC="$RESOLVED" ;;
    esac
fi

UPDATED_PROFILE=""
if [[ ":$PATH:" != *":${INSTALL_DIR}:"* ]]; then
    SHELL_NAME=$(basename "${SHELL:-bash}")
    case "$SHELL_NAME" in
        zsh)  PROFILE="$HOME/.zshrc" ;;
        bash) PROFILE="$HOME/.bashrc" ;;
        *)    PROFILE="$HOME/.profile" ;;
    esac

    if [ -n "$EXISTING_FUNC" ]; then
        echo "export PATH=\"\$PATH:${INSTALL_DIR}\"" >> "$PROFILE"
        export PATH="${PATH}:${INSTALL_DIR}"
    else
        echo "export PATH=\"${INSTALL_DIR}:\$PATH\"" >> "$PROFILE"
        export PATH="${INSTALL_DIR}:${PATH}"
    fi
    echo "Added ${INSTALL_DIR} to PATH in ${PROFILE}."
    UPDATED_PROFILE="$PROFILE"
fi

echo "func CLI ${VERSION} installed to ${INSTALL_DIR}"
"${INSTALL_DIR}/func" --version

# --- Telemetry notice ---

echo ""
echo "Telemetry"
echo "---------"
echo "The Azure Functions CLI collects usage data in order to help us improve your experience."
echo "The data is anonymous and doesn't include any user specific or personal information. The data is collected by Microsoft."
echo ""
echo "You can opt-out of telemetry by setting the FUNC_CLI_TELEMETRY_OPTOUT environment variable to any value other than 'no', 'n', '0', 'false', or 'off' using your favorite shell."

# --- Side-by-side notice ---

echo ""
echo "Side-by-side with Core Tools v4"
echo "-------------------------------"
if [ -n "$EXISTING_FUNC" ]; then
    echo "Detected an existing 'func' at ${EXISTING_FUNC}, leaving it as the default."
    echo "Use 'func5' to invoke v5; 'func' will continue to invoke the existing install."
else
    echo "No existing 'func' was found on PATH, so 'func' and 'func5' both invoke v5."
    echo "If you later install Core Tools v4, use 'func5' to keep invoking v5."
fi

# --- Bug bash env vars ---

if [ "$BUGBASH" = "true" ]; then
    SHELL_NAME=$(basename "${SHELL:-bash}")
    case "$SHELL_NAME" in
        zsh)  BUGBASH_PROFILE="$HOME/.zshrc" ;;
        bash) BUGBASH_PROFILE="$HOME/.bashrc" ;;
        *)    BUGBASH_PROFILE="$HOME/.profile" ;;
    esac

    {
        echo ""
        echo "# Azure Functions CLI bug bash env vars"
        echo "export FUNC_CLI_WORKLOADS_SOURCE=\"${BUGBASH_WORKLOADS_SOURCE}\""
        echo "export FUNC_CLI_QUICKSTART_MANIFEST_URL=\"${BUGBASH_QUICKSTART_MANIFEST_URL}\""
        echo "export FUNC_CLI_WORKLOADS_PRERELEASE=true"
    } >> "$BUGBASH_PROFILE"

    export FUNC_CLI_WORKLOADS_SOURCE="${BUGBASH_WORKLOADS_SOURCE}"
    export FUNC_CLI_QUICKSTART_MANIFEST_URL="${BUGBASH_QUICKSTART_MANIFEST_URL}"
    export FUNC_CLI_WORKLOADS_PRERELEASE=true

    echo ""
    echo -e "\033[33m========================================================================\033[0m"
    echo -e "\033[33m  BUG BASH MODE: required environment variables have been set\033[0m"
    echo -e "\033[33m========================================================================\033[0m"
    echo -e "\033[33mAdded to current session and appended to ${BUGBASH_PROFILE}:\033[0m"
    echo ""
    echo "  export FUNC_CLI_WORKLOADS_SOURCE=\"${BUGBASH_WORKLOADS_SOURCE}\""
    echo "  export FUNC_CLI_QUICKSTART_MANIFEST_URL=\"${BUGBASH_QUICKSTART_MANIFEST_URL}\""
    echo "  export FUNC_CLI_WORKLOADS_PRERELEASE=true"
    echo ""
    echo -e "\033[33mWARNING: these env vars MUST be set in your shell for the bug bash.\033[0m"
    echo -e "\033[33mIf you open a new terminal session (or a shell that doesn't load\033[0m"
    echo -e "\033[33m${BUGBASH_PROFILE}), re-run the three exports above before using func.\033[0m"
    echo -e "\033[33m========================================================================\033[0m"
fi

# --- Reload shell reminder ---

echo ""
echo "Reload your shell"
echo "-----------------"
if [ -n "$UPDATED_PROFILE" ]; then
    echo -e "\033[33mReload your shell so 'func5' is on PATH in this session:\033[0m"
    echo "  source ${UPDATED_PROFILE}"
    echo "Or open a new terminal window."
else
    SHELL_NAME=$(basename "${SHELL:-bash}")
    case "$SHELL_NAME" in
        zsh)  PROFILE_HINT="$HOME/.zshrc" ;;
        bash) PROFILE_HINT="$HOME/.bashrc" ;;
        *)    PROFILE_HINT="$HOME/.profile" ;;
    esac
    echo "If 'func5' isn't found in your current shell, open a new terminal or run:"
    echo "  source ${PROFILE_HINT}"
fi
