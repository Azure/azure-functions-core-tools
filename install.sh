#!/usr/bin/env bash

# install.sh - Download and install the Azure Functions CLI for the current platform
# Usage: ./install.sh [OPTIONS]
#        curl -sSL https://aka.ms/func-cli/install.sh | bash
#        curl -sSL https://aka.ms/func-cli/install.sh | bash -s -- [OPTIONS]

set -euo pipefail

# --- Constants ---

readonly USER_AGENT="func-cli-install.sh/1.0"
readonly ARCHIVE_DOWNLOAD_TIMEOUT_SEC=600
readonly HEAD_REQUEST_TIMEOUT_SEC=60
readonly DEFAULT_INSTALL_DIR="$HOME/.azure-functions"
readonly CDN_BASE_URL="https://cdn.functions.azure.com"
readonly VERSION_MANIFEST_URL="${CDN_BASE_URL}/public/cli/v5/version.json"
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly RESET='\033[0m'

# --- Variables (defaults applied after arg parsing) ---

INSTALL_DIR="${INSTALL_DIR:-}"
VERSION="${VERSION:-}"
PRERELEASE="${PRERELEASE:-false}"
FORCE="${FORCE:-false}"
SKIP_PATH=false
KEEP_ARCHIVE=false
DRY_RUN=false
VERBOSE=false
SHOW_HELP=false

# --- Logging ---

say_info() {
    echo -e "$1" >&2
}

say_success() {
    echo -e "${GREEN}$1${RESET}" >&2
}

say_warn() {
    echo -e "${YELLOW}Warning: $1${RESET}" >&2
}

say_error() {
    echo -e "${RED}Error: $1${RESET}" >&2
}

say_verbose() {
    if [[ "$VERBOSE" == true ]]; then
        echo -e "${YELLOW}$1${RESET}" >&2
    fi
}

# --- Help ---

show_help() {
    cat << 'EOF'
Azure Functions CLI installer

DESCRIPTION:
    Downloads and installs the func CLI for the current platform from the Azure Functions CDN.
    Automatically updates your shell profile so func is on PATH in new terminals.

USAGE:
    ./install.sh [OPTIONS]

OPTIONS:
    -i, --install-path PATH     Directory to install the CLI (default: $HOME/.azure-functions)
        --version VERSION       Specific version to install (default: latest 5.x stable)
        --prerelease            Include pre-release versions when resolving latest
    -f, --force                 Overwrite an existing installation
        --skip-path             Do not update PATH or shell profile
    -k, --keep-archive          Keep the downloaded archive and temp directory after install
        --dry-run               Show what would happen without making changes
    -v, --verbose               Enable verbose output
    -h, --help                  Show this help message

ENVIRONMENT VARIABLES (back-compat, flags take precedence):
    INSTALL_DIR, VERSION, PRERELEASE=true, FORCE=true

GITHUB ACTIONS:
    When GITHUB_ACTIONS=true, the install dir is also appended to $GITHUB_PATH so
    func is available in subsequent workflow steps.

EXAMPLES:
    ./install.sh
    ./install.sh --install-path ~/bin/func
    ./install.sh --version 5.0.0 --force
    ./install.sh --prerelease

    # Piped execution:
    curl -sSL https://aka.ms/func-cli/install.sh | bash
    curl -sSL https://aka.ms/func-cli/install.sh | bash -s -- --prerelease
    curl -sSL https://aka.ms/func-cli/install.sh | bash -s -- --install-path ~/bin/func
EOF
}

# --- Argument parsing ---

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            -i|--install-path)
                [[ $# -lt 2 || -z "$2" ]] && { say_error "Option '$1' requires a value"; exit 1; }
                INSTALL_DIR="$2"; shift 2 ;;
            --version)
                [[ $# -lt 2 || -z "$2" ]] && { say_error "Option '$1' requires a value"; exit 1; }
                VERSION="$2"; shift 2 ;;
            --prerelease) PRERELEASE=true; shift ;;
            -f|--force)   FORCE=true; shift ;;
            --skip-path)  SKIP_PATH=true; shift ;;
            -k|--keep-archive) KEEP_ARCHIVE=true; shift ;;
            --dry-run)    DRY_RUN=true; shift ;;
            -v|--verbose) VERBOSE=true; shift ;;
            -h|--help)    SHOW_HELP=true; shift ;;
            *)
                say_error "Unknown option: $1"
                say_info "Use --help for usage information."
                exit 1
                ;;
        esac
    done
}

parse_args "$@"

if [[ "$SHOW_HELP" == true ]]; then
    show_help
    exit 0
fi

INSTALL_DIR="${INSTALL_DIR:-$DEFAULT_INSTALL_DIR}"

# --- Platform detection ---

detect_os() {
    case "$(uname -s)" in
        Linux*)
            if command -v ldd >/dev/null 2>&1 && ldd --version 2>&1 | grep -q musl; then
                printf "linux-musl"
            else
                printf "linux"
            fi
            ;;
        Darwin*) printf "osx" ;;
        CYGWIN*|MINGW*|MSYS*) printf "win" ;;
        *) return 1 ;;
    esac
}

detect_arch() {
    case "$(uname -m)" in
        x86_64|amd64)  printf "x64" ;;
        arm64|aarch64) printf "arm64" ;;
        *) return 1 ;;
    esac
}

OS=$(detect_os) || { say_error "Unsupported OS: $(uname -s)"; exit 1; }
ARCH=$(detect_arch) || { say_error "Unsupported architecture: $(uname -m)"; exit 1; }

if [[ "$OS" == "linux-musl" ]]; then
    say_warn "Detected musl libc (Alpine and similar). The published Linux assets target glibc; install may fail."
    OS="linux"
fi

RID="${OS}-${ARCH}"
say_verbose "Resolved platform RID: ${RID}"

# --- HTTP helpers ---

# Hardened curl: TLS-pinned, retries, timeouts, capped redirects.
secure_curl() {
    local url="$1"
    local output_file="$2"
    local timeout="${3:-$ARCHIVE_DOWNLOAD_TIMEOUT_SEC}"
    local method="${4:-GET}"

    local args=(
        --fail
        --show-error
        --location
        --tlsv1.2
        --max-time "$timeout"
        --user-agent "$USER_AGENT"
        --max-redirs 10
        --retry 5
        --retry-delay 1
        --retry-max-time 60
        --request "$method"
    )

    if [[ "$method" == "HEAD" ]]; then
        args+=(--silent --head)
    elif [[ "$VERBOSE" == true ]]; then
        args+=(--progress-bar)
    else
        args+=(--silent)
    fi

    if [[ "$method" == "GET" ]]; then
        args+=(--output "$output_file")
    fi

    say_verbose "curl ${args[*]} $url"
    curl "${args[@]}" "$url"
}

# HEAD-probe to make sure we're about to download a binary, not an HTML error page.
validate_content_type() {
    local url="$1"
    local headers

    say_verbose "Validating content type for $url"

    if ! headers=$(secure_curl "$url" /dev/null "$HEAD_REQUEST_TIMEOUT_SEC" "HEAD" 2>&1); then
        say_verbose "HEAD request failed; proceeding with download anyway."
        return 0
    fi

    # CDN asset URLs may return one or more 3xx redirects followed by a final 2xx.
    # Look only at the final response block.
    local final_headers
    final_headers=$(printf "%s\n" "$headers" | awk '
        /^HTTP(\/| )[0-9]/ { block = $0 "\n"; next }
        { block = block $0 "\n" }
        END { printf "%s", block }')

    if echo "$final_headers" | grep -qi "content-type:.*text/html"; then
        say_error "Server returned HTML instead of an archive. URL: $url"
        return 1
    fi
    return 0
}

# --- Resolve version ---

# Extract a string field from a small flat JSON object without requiring jq.
json_field() {
    local json="$1" field="$2"
    printf '%s' "$json" | tr -d '\n' \
        | sed -nE "s/.*\"${field}\"[[:space:]]*:[[:space:]]*\"([^\"]*)\".*/\1/p"
}

# Returns 0 (true) if the first SemVer is strictly greater than the second by
# precedence (SemVer 2.0 §11), 1 otherwise.
semver_gt() {
    local a="${1#[vV]}" b="${2#[vV]}"
    a="${a%%+*}"; b="${b%%+*}"

    local a_core="${a%%-*}" b_core="${b%%-*}"
    local a_pre="" b_pre=""
    [[ "$a" == *-* ]] && a_pre="${a#*-}"
    [[ "$b" == *-* ]] && b_pre="${b#*-}"

    local IFS=.
    local -a an=($a_core) bn=($b_core)
    local i
    for i in 0 1 2; do
        local av="${an[i]:-0}" bv="${bn[i]:-0}"
        ((av > bv)) && return 0
        ((av < bv)) && return 1
    done

    # Cores are equal: a build without a prerelease outranks one with it.
    [[ -z "$a_pre" && -z "$b_pre" ]] && return 1
    [[ -z "$a_pre" ]] && return 0
    [[ -z "$b_pre" ]] && return 1

    local -a ai=($a_pre) bi=($b_pre)
    local n=${#ai[@]}
    (( ${#bi[@]} > n )) && n=${#bi[@]}
    for ((i = 0; i < n; i++)); do
        (( i >= ${#ai[@]} )) && return 1
        (( i >= ${#bi[@]} )) && return 0
        local x="${ai[i]}" y="${bi[i]}"
        if [[ "$x" =~ ^[0-9]+$ && "$y" =~ ^[0-9]+$ ]]; then
            ((10#$x > 10#$y)) && return 0
            ((10#$x < 10#$y)) && return 1
        elif [[ "$x" =~ ^[0-9]+$ ]]; then
            return 1
        elif [[ "$y" =~ ^[0-9]+$ ]]; then
            return 0
        else
            [[ "$x" > "$y" ]] && return 0
            [[ "$x" < "$y" ]] && return 1
        fi
    done
    return 1
}

resolve_version() {
    if [[ -n "$VERSION" ]]; then
        # CDN artifacts are named with a bare SemVer (no leading 'v').
        VERSION="${VERSION#v}"
        VERSION="${VERSION#V}"
        return 0
    fi

    if [[ "$PRERELEASE" == true ]]; then
        say_info "Resolving latest 5.x pre-release from CDN..."
    else
        say_info "Resolving latest stable 5.x release from CDN..."
    fi

    local manifest_json
    manifest_json=$(curl -sSL --fail --tlsv1.2 --user-agent "$USER_AGENT" \
        --max-time "$HEAD_REQUEST_TIMEOUT_SEC" "$VERSION_MANIFEST_URL") || {
        say_error "Could not fetch version manifest from ${VERSION_MANIFEST_URL}"
        exit 1
    }

    local stable preview
    stable=$(json_field "$manifest_json" "stable")
    preview=$(json_field "$manifest_json" "preview")

    if [[ "$PRERELEASE" == true && -n "$preview" ]] && semver_gt "$preview" "$stable"; then
        VERSION="$preview"
    else
        VERSION="$stable"
    fi

    if [[ -z "$VERSION" ]]; then
        say_error "Could not resolve a 5.x version from the CDN manifest."
        exit 1
    fi
}

resolve_version

ASSET_NAME="Azure.Functions.Cli.${RID}.${VERSION}.tar.gz"
DOWNLOAD_URL="${CDN_BASE_URL}/public/cli/v5/${VERSION}/${ASSET_NAME}"

say_info "Installing func CLI ${VERSION} (${RID})..."

# --- Check existing install ---

if [[ -f "${INSTALL_DIR}/func" && "$FORCE" != true ]]; then
    say_error "func CLI is already installed at ${INSTALL_DIR}."
    say_error "To overwrite, re-run with --force."
    exit 0
fi

# --- Download and extract ---

TEMP_DIR=$(mktemp -d)
if [[ "$KEEP_ARCHIVE" != true ]]; then
    trap 'rm -rf "$TEMP_DIR"' EXIT
else
    say_info "Keeping temp dir: $TEMP_DIR"
fi

if [[ "$DRY_RUN" == true ]]; then
    say_info "[DRY RUN] Would download ${DOWNLOAD_URL} to ${TEMP_DIR}/${ASSET_NAME}"
    say_info "[DRY RUN] Would extract to ${INSTALL_DIR}"
else
    validate_content_type "$DOWNLOAD_URL" || exit 1
    say_info "Downloading ${ASSET_NAME}..."
    secure_curl "$DOWNLOAD_URL" "${TEMP_DIR}/${ASSET_NAME}"
    mkdir -p "$INSTALL_DIR"
    tar -xzf "${TEMP_DIR}/${ASSET_NAME}" -C "$INSTALL_DIR"
    chmod +x "${INSTALL_DIR}/func" 2>/dev/null || true

    if [[ "$OS" == "osx" ]]; then
        xattr -d com.apple.quarantine "${INSTALL_DIR}/func" 2>/dev/null || true
    fi

    # Drop a func5 wrapper so v5 can be invoked side-by-side with a v4 `func` on PATH.
    cat > "${INSTALL_DIR}/func5" <<'EOF'
#!/usr/bin/env bash
exec "$(dirname "$0")/func" "$@"
EOF
    chmod +x "${INSTALL_DIR}/func5"
fi

# --- PATH / shell profile ---

# Resolve a shell config file. Walk a list per shell and pick the first that exists;
# if none exist, fall back to the canonical default for that shell.
detect_shell_config() {
    local shell_name="${1:-bash}"
    local xdg_config_home="${XDG_CONFIG_HOME:-$HOME/.config}"
    local candidates default_file

    case "$shell_name" in
        zsh)
            candidates=("$HOME/.zshrc" "$HOME/.zshenv" "$xdg_config_home/zsh/.zshrc")
            default_file="$HOME/.zshrc"
            ;;
        fish)
            candidates=("$xdg_config_home/fish/config.fish")
            default_file="$xdg_config_home/fish/config.fish"
            ;;
        sh|dash|ash)
            candidates=("$HOME/.profile")
            default_file="$HOME/.profile"
            ;;
        bash|*)
            candidates=("$HOME/.bashrc" "$HOME/.bash_profile" "$HOME/.profile")
            default_file="$HOME/.bashrc"
            ;;
    esac

    for f in "${candidates[@]}"; do
        if [[ -f "$f" ]]; then
            printf "%s" "$f"
            return 0
        fi
    done
    printf "%s" "$default_file"
}

build_path_export() {
    local shell_name="$1"
    local dir="$2"
    local prepend="$3"
    case "$shell_name" in
        fish)
            if [[ "$prepend" == true ]]; then
                printf "set -gx PATH %s \$PATH" "$dir"
            else
                printf "set -gx PATH \$PATH %s" "$dir"
            fi
            ;;
        *)
            if [[ "$prepend" == true ]]; then
                printf "export PATH=\"%s:\$PATH\"" "$dir"
            else
                printf "export PATH=\"\$PATH:%s\"" "$dir"
            fi
            ;;
    esac
}

# Detect a pre-existing 'func' outside our install dir (e.g. Core Tools v4).
# If present: APPEND so v4 keeps winning and only 'func5' resolves to v5.
# Otherwise: PREPEND so new users get 'func' = v5 by default.
EXISTING_FUNC=""
if command -v func >/dev/null 2>&1; then
    RESOLVED=$(command -v func)
    case "$RESOLVED" in
        "${INSTALL_DIR}/"*) ;;
        *) EXISTING_FUNC="$RESOLVED" ;;
    esac
fi

UPDATED_PROFILE=""
if [[ "$SKIP_PATH" == true ]]; then
    say_info "Skipping PATH update (--skip-path)."
elif [[ ":$PATH:" != *":${INSTALL_DIR}:"* ]]; then
    SHELL_NAME=$(basename "${SHELL:-bash}")
    PROFILE=$(detect_shell_config "$SHELL_NAME")
    PREPEND=true
    [[ -n "$EXISTING_FUNC" ]] && PREPEND=false
    EXPORT_LINE=$(build_path_export "$SHELL_NAME" "$INSTALL_DIR" "$PREPEND")

    if [[ "$DRY_RUN" == true ]]; then
        say_info "[DRY RUN] Would append to ${PROFILE}: ${EXPORT_LINE}"
    else
        mkdir -p "$(dirname "$PROFILE")"
        if [[ -f "$PROFILE" ]] && grep -Fxq "$EXPORT_LINE" "$PROFILE"; then
            say_verbose "Export already present in $PROFILE"
        else
            printf "\n# Added by Azure Functions CLI installer\n%s\n" "$EXPORT_LINE" >> "$PROFILE"
        fi
        if [[ "$PREPEND" == true ]]; then
            export PATH="${INSTALL_DIR}:${PATH}"
        else
            export PATH="${PATH}:${INSTALL_DIR}"
        fi
        UPDATED_PROFILE="$PROFILE"
    fi
fi

# GitHub Actions: make func available in subsequent workflow steps.
if [[ "${GITHUB_ACTIONS:-}" == "true" && -n "${GITHUB_PATH:-}" ]]; then
    if [[ "$DRY_RUN" == true ]]; then
        say_info "[DRY RUN] Would append ${INSTALL_DIR} to \$GITHUB_PATH"
    else
        echo "$INSTALL_DIR" >> "$GITHUB_PATH"
        say_info "Appended ${INSTALL_DIR} to \$GITHUB_PATH for subsequent workflow steps."
    fi
fi

if [[ "$DRY_RUN" == true ]]; then
    say_success "[DRY RUN] func CLI ${VERSION} would be installed to ${INSTALL_DIR}"
    exit 0
fi

say_success "func CLI ${VERSION} successfully installed to: ${INSTALL_DIR}/func"

if [[ -n "$UPDATED_PROFILE" ]]; then
    say_success "Successfully added func to \$PATH in ${UPDATED_PROFILE}"
fi

# --- Telemetry notice ---

say_info ""
say_info "Telemetry"
say_info "---------"
say_info ""
say_info "The Azure Functions CLI collects usage data. It is collected by Microsoft and is used to help us improve your experience. You can opt out of telemetry by setting the FUNC_CLI_TELEMETRY_OPTOUT environment variable to '1' or 'true' using your preferred shell."

# --- Side-by-side notice ---

if [[ -n "$EXISTING_FUNC" ]]; then
    say_info ""
    say_info "Side-by-side notice"
    say_info "-------------------"
    say_info ""
    say_info "Detected an existing 'func' at ${EXISTING_FUNC}, leaving it as the default."
    say_info "Use 'func5' to invoke v5."
fi

# --- Reload shell reminder ---

if [[ "$SKIP_PATH" != true && -n "$UPDATED_PROFILE" ]]; then
    say_info ""
    say_info "Reload shell"
    say_info "------------"
    say_info ""
    say_info "To use the func CLI in new terminal sessions, restart your terminal or run:"
    say_info "  source ${UPDATED_PROFILE}"
fi

