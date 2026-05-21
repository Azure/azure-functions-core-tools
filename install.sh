#!/usr/bin/env bash
set -euo pipefail

# Azure Functions Core Tools CLI installer
# Usage: curl -sSL https://aka.ms/func-cli/install.sh | bash

REPO="Azure/azure-functions-core-tools"
API_BASE="https://api.github.com/repos/${REPO}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.azure-functions}"
VERSION="${VERSION:-}"

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
    echo "Resolving latest 5.x release..."
    VERSION=$(curl -sSL "${API_BASE}/releases?per_page=50" \
        | grep -o '"tag_name":"5\.[^"]*"' \
        | head -1 \
        | cut -d'"' -f4)

    if [ -z "$VERSION" ]; then
        echo "Error: Could not find a 5.x release." >&2
        exit 1
    fi
fi

echo "Installing func CLI ${VERSION} (${OS}-${ARCH})..."

# --- Download and extract ---

DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${VERSION}/${ASSET_NAME}"
TEMP_DIR=$(mktemp -d)
trap 'rm -rf "$TEMP_DIR"' EXIT

curl -sSL -o "${TEMP_DIR}/${ASSET_NAME}" "$DOWNLOAD_URL"
mkdir -p "$INSTALL_DIR"
tar -xzf "${TEMP_DIR}/${ASSET_NAME}" -C "$INSTALL_DIR"

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
