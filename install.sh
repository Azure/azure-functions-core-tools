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
        | sed 's/.*"tag_name":"\([^"]*\)".*/tag:\1/; s/.*"prerelease":\(.*\)/pre:\1/' \
        | paste - - \
        | awk -F'\t' -v include_pre="$PRERELEASE" '
            {
                split($1, a, ":"); tag = a[2]
                split($2, b, ":"); pre = b[2]
                if (tag ~ /^v5\./ && (include_pre == "true" || pre == "false")) {
                    print tag; exit
                }
            }')

    if [ -z "$VERSION" ]; then
        if [ "$PRERELEASE" != "true" ]; then
            PRE_VERSIONS=$(echo "$RELEASES_JSON" \
                | tr ',' '\n' \
                | grep -E '"tag_name"|"prerelease"' \
                | sed 's/.*"tag_name":"\([^"]*\)".*/tag:\1/; s/.*"prerelease":\(.*\)/pre:\1/' \
                | paste - - \
                | awk -F'\t' '
                    {
                        split($1, a, ":"); tag = a[2]
                        split($2, b, ":"); pre = b[2]
                        if (tag ~ /^v5\./ && pre == "true") { print tag; count++; if (count >= 5) exit }
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

# --- Update PATH ---

if [[ ":$PATH:" != *":${INSTALL_DIR}:"* ]]; then
    SHELL_NAME=$(basename "${SHELL:-bash}")
    case "$SHELL_NAME" in
        zsh)  PROFILE="$HOME/.zshrc" ;;
        bash) PROFILE="$HOME/.bashrc" ;;
        *)    PROFILE="$HOME/.profile" ;;
    esac

    echo "export PATH=\"${INSTALL_DIR}:\$PATH\"" >> "$PROFILE"
    echo "Added ${INSTALL_DIR} to PATH in ${PROFILE}."
    export PATH="${INSTALL_DIR}:${PATH}"
fi

echo "func CLI ${VERSION} installed to ${INSTALL_DIR}"
func --version
