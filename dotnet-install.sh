#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#
# Original file from https://github.com/dotnet/cli/blob/80d542b8f4eff847a0f72dc8f2c2a29851272778/scripts/obtain/dotnet-install.sh

# Stop script on NZEC
set -e
# Stop script if unbound variable found (use ${var:-} if intentional)
set -u
# By default cmd1 | cmd2 returns exit code of cmd2 regardless of cmd1 success
# This is causing it to fail
set -o pipefail

# Use in the the functions: eval $invocation
invocation='say_verbose "Calling: ${yellow:-}${FUNCNAME[0]} ${green:-}$*${normal:-}"'

# standard output may be used as a return value in the functions
# we need a way to write text on the screen in the functions so that
# it won't interfere with the return value.
# Exposing stream 3 as a pipe to standard output of the script itself
exec 3>&1

# Setup some colors to use. These need to work in fairly limited shells, like the Ubuntu Docker container where there are only 8 colors.
# See if stdout is a terminal
if [ -t 1 ] && command -v tput > /dev/null; then
    # see if it supports colors
    ncolors=$(tput colors)
    if [ -n "$ncolors" ] && [ $ncolors -ge 8 ]; then
        bold="$(tput bold       || echo)"
        normal="$(tput sgr0     || echo)"
        black="$(tput setaf 0   || echo)"
        red="$(tput setaf 1     || echo)"
        green="$(tput setaf 2   || echo)"
        yellow="$(tput setaf 3  || echo)"
        blue="$(tput setaf 4    || echo)"
        magenta="$(tput setaf 5 || echo)"
        cyan="$(tput setaf 6    || echo)"
        white="$(tput setaf 7   || echo)"
    fi
fi

say_warning() {
    printf "%b\n" "${yellow:-}dotnet_install: Warning: $1${normal:-}"
}

say_err() {
    printf "%b\n" "${red:-}dotnet_install: Error: $1${normal:-}" >&2
}

say() {
    # using stream 3 (defined in the beginning) to not interfere with stdout of functions
    # which may be used as return value
    printf "%b\n" "${cyan:-}dotnet-install:${normal:-} $1" >&3
}

say_verbose() {
    if [ "$verbose" = true ]; then
        say "$1"
    fi
}

# This platform list is finite - if the SDK/Runtime has supported Linux distribution-specific assets,
#   then and only then should the Linux distribution appear in this list.
# Adding a Linux distribution to this list does not imply distribution-specific support.
get_legacy_os_name_from_platform() {
    eval $invocation

    platform="$1"
    case "$platform" in
        "centos.7")
            echo "centos"
            return 0
            ;;
        "debian.8")
            echo "debian"
            return 0
            ;;
        "fedora.23")
            echo "fedora.23"
            return 0
            ;;
        "fedora.27")
            echo "fedora.27"
            return 0
            ;;
        "fedora.24")
            echo "fedora.24"
            return 0
            ;;
        "opensuse.13.2")
            echo "opensuse.13.2"
            return 0
            ;;
        "opensuse.42.1")
            echo "opensuse.42.1"
            return 0
            ;;
        "opensuse.42.3")
            echo "opensuse.42.3"
            return 0
            ;;
        "rhel.7"*)
            echo "rhel"
            return 0
            ;;
        "ubuntu.14.04")
            echo "ubuntu"
            return 0
            ;;
        "ubuntu.16.04")
            echo "ubuntu.16.04"
            return 0
            ;;
        "ubuntu.16.10")
            echo "ubuntu.16.10"
            return 0
            ;;
        "ubuntu.18.04")
            echo "ubuntu.18.04"
            return 0
            ;;
        "alpine.3.4.3")
            echo "alpine"
            return 0
            ;;
    esac
    return 1
}

get_linux_platform_name() {
    eval $invocation

    if [ -n "$runtime_id" ]; then
        echo "${runtime_id%-*}"
        return 0
    else
        if [ -e /etc/os-release ]; then
            . /etc/os-release
            echo "$ID.$VERSION_ID"
            return 0
        elif [ -e /etc/redhat-release ]; then
            local redhatRelease=$(</etc/redhat-release)
            if [[ $redhatRelease == "CentOS release 6."* || $redhatRelease == "Red Hat Enterprise Linux Server release 6."* ]]; then
                echo "rhel.6"
                return 0
            fi
        fi
    fi

    say_verbose "Linux specific platform name and version could not be detected: UName = $uname"
    return 1
}

get_current_os_name() {
    eval $invocation

    local uname=$(uname)
    if [ "$uname" = "Darwin" ]; then
        echo "osx"
        return 0
    elif [ "$uname" = "Linux" ]; then
        local linux_platform_name
        linux_platform_name="$(get_linux_platform_name)" || { echo "linux" && return 0 ; }

        if [[ $linux_platform_name == "rhel.6" ]]; then
            echo $linux_platform_name
            return 0
        elif [[ $linux_platform_name == alpine* ]]; then
            echo "linux-musl"
            return 0
        else
            echo "linux"
            return 0
        fi
    fi

    say_err "OS name could not be detected: UName = $uname"
    return 1
}

get_legacy_os_name() {
    eval $invocation

    local uname=$(uname)
    if [ "$uname" = "Darwin" ]; then
        echo "osx"
        return 0
    elif [ -n "$runtime_id" ]; then
        echo $(get_legacy_os_name_from_platform "${runtime_id%-*}" || echo "${runtime_id%-*}")
        return 0
    else
        if [ -e /etc/os-release ]; then
            . /etc/os-release
            os=$(get_legacy_os_name_from_platform "$ID.$VERSION_ID" || echo "")
            if [ -n "$os" ]; then
                echo "$os"
                return 0
            fi
        fi
    fi

    say_verbose "Distribution specific OS name and version could not be detected: UName = $uname"
    return 1
}

machine_has() {
    eval $invocation

    hash "$1" > /dev/null 2>&1
    return $?
}


check_min_reqs() {
    local hasMinimum=false
    if machine_has "curl"; then
        hasMinimum=true
    elif machine_has "wget"; then
        hasMinimum=true
    fi

    if [ "$hasMinimum" = "false" ]; then
        say_err "curl (recommended) or wget are required to download dotnet. Install missing prerequisite to proceed."
        return 1
    fi
    return 0
}

check_pre_reqs() {
    eval $invocation

    if [ "${DOTNET_INSTALL_SKIP_PREREQS:-}" = "1" ]; then
        return 0
    fi

    if [ "$(uname)" = "Linux" ]; then
        if [ ! -x "$(command -v ldconfig)" ]; then
            echo "ldconfig is not in PATH, trying /sbin/ldconfig."
            LDCONFIG_COMMAND="/sbin/ldconfig"
        else
            LDCONFIG_COMMAND="ldconfig"
        fi

        local librarypath=${LD_LIBRARY_PATH:-}
        LDCONFIG_COMMAND="$LDCONFIG_COMMAND -NXv ${librarypath//:/ }"

        [ -z "$($LDCONFIG_COMMAND 2>/dev/null | grep libunwind)" ] && say_warning "Unable to locate libunwind. Probable prerequisite missing; please install libunwind."
        [ -z "$($LDCONFIG_COMMAND 2>/dev/null | grep libssl)" ] && say_warning "Unable to locate libssl. Probable prerequisite missing; please install libssl."
        [ -z "$($LDCONFIG_COMMAND 2>/dev/null | grep libicu)" ] && say_warning "Unable to locate libicu. Probable prerequisite missing; please install libicu."
        [ -z "$($LDCONFIG_COMMAND 2>/dev/null | grep -F libcurl.so)" ] && say_warning "Unable to locate libcurl. Probable prerequisite missing; please install libcurl."
    fi

    return 0
}

# args:
# input - $1
to_lowercase() {
    #eval $invocation

    echo "$1" | tr '[:upper:]' '[:lower:]'
    return 0
}

# args:
# input - $1
remove_trailing_slash() {
    #eval $invocation

    local input="${1:-}"
    echo "${input%/}"
    return 0
}

# args:
# input - $1
remove_beginning_slash() {
    #eval $invocation

    local input="${1:-}"
    echo "${input#/}"
    return 0
}

# args:
# root_path - $1
# child_path - $2 - this parameter can be empty
combine_paths() {
    eval $invocation

    # TODO: Consider making it work with any number of paths. For now:
    if [ ! -z "${3:-}" ]; then
        say_err "combine_paths: Function takes two parameters."
        return 1
    fi

    local root_path="$(remove_trailing_slash "$1")"
    local child_path="$(remove_beginning_slash "${2:-}")"
    say_verbose "combine_paths: root_path=$root_path"
    say_verbose "combine_paths: child_path=$child_path"
    echo "$root_path/$child_path"
    return 0
}

get_machine_architecture() {
    eval $invocation

    if command -v uname > /dev/null; then
        CPUName=$(uname -m)
        case $CPUName in
        armv7l)
            echo "arm"
            return 0
            ;;
        aarch64)
            echo "arm64"
            return 0
            ;;
        esac
    fi

    # Always default to 'x64'
    echo "x64"
    return 0
}

# args:
# architecture - $1
get_normalized_architecture_from_architecture() {
    eval $invocation

    local architecture="$(to_lowercase "$1")"
    case "$architecture" in
        \<auto\>)
            echo "$(get_normalized_architecture_from_architecture "$(get_machine_architecture)")"
            return 0
            ;;
        amd64|x64)
            echo "x64"
            return 0
            ;;
        arm)
            echo "arm"
            return 0
            ;;
        arm64)
            echo "arm64"
            return 0
            ;;
    esac

    say_err "Architecture \`$architecture\` not supported. If you think this is a bug, please report it at https://github.com/dotnet/cli/issues"
    return 1
}

# version_info is a conceptual two line string representing commit hash and 4-part version
# format:
# Line 1: # commit_hash
# Line 2: # 4-part version

# args:
# version_text - stdin
get_version_from_version_info() {
    eval $invocation

    cat | tail -n 1 | sed 's/\r$//'
    return 0
}

# args:
# version_text - stdin
get_commit_hash_from_version_info() {
    eval $invocation

    cat | head -n 1 | sed 's/\r$//'
    return 0
}

# args:
# install_root - $1
# relative_path_to_package - $2
# specific_version - $3
is_dotnet_package_installed() {
    eval $invocation

    local install_root="$1"
    local relative_path_to_package="$2"
    local specific_version="${3//[$'\t\r\n']}"

    local dotnet_package_path="$(combine_paths "$(combine_paths "$install_root" "$relative_path_to_package")" "$specific_version")"
    say_verbose "is_dotnet_package_installed: dotnet_package_path=$dotnet_package_path"

    if [ -d "$dotnet_package_path" ]; then
        return 0
    else
        return 1
    fi
}

# args:
# azure_feed - $1
# channel - $2
# normalized_architecture - $3
# coherent - $4
get_latest_version_info() {
    eval $invocation

    local azure_feed="$1"
    local channel="$2"
    local normalized_architecture="$3"
    local coherent="$4"

    local version_file_url=null
    if [[ "$runtime" == "dotnet" ]]; then
        version_file_url="$uncached_feed/Runtime/$channel/latest.version"
    elif [[ "$runtime" == "aspnetcore" ]]; then
        version_file_url="$uncached_feed/aspnetcore/Runtime/$channel/latest.version"
    elif [ -z "$runtime" ]; then
        if [ "$coherent" = true ]; then
            version_file_url="$uncached_feed/Sdk/$channel/latest.coherent.version"
        else
            version_file_url="$uncached_feed/Sdk/$channel/latest.version"
        fi
    else
        say_err "Invalid value for \$runtime"
        return 1
    fi
    say_verbose "get_latest_version_info: latest url: $version_file_url"

    download "$version_file_url"
    return $?
}

# args:
# azure_feed - $1
# channel - $2
# normalized_architecture - $3
# version - $4
get_specific_version_from_version() {
    eval $invocation

    local azure_feed="$1"
    local channel="$2"
    local normalized_architecture="$3"
    local version="$(to_lowercase "$4")"

    case "$version" in
        latest)
            local version_info
            version_info="$(get_latest_version_info "$azure_feed" "$channel" "$normalized_architecture" false)" || return 1
            say_verbose "get_specific_version_from_version: version_info=$version_info"
            echo "$version_info" | get_version_from_version_info
            return 0
            ;;
        coherent)
            local version_info
            version_info="$(get_latest_version_info "$azure_feed" "$channel" "$normalized_architecture" true)" || return 1
            say_verbose "get_specific_version_from_version: version_info=$version_info"
            echo "$version_info" | get_version_from_version_info
            return 0
            ;;
        *)
            echo "$version"
            return 0
            ;;
    esac
}

# args:
# azure_feed - $1
# channel - $2
# normalized_architecture - $3
# specific_version - $4
construct_download_link() {
    eval $invocation

    local azure_feed="$1"
    local channel="$2"
    local normalized_architecture="$3"
    local specific_version="${4//[$'\t\r\n']}"

    local osname
    osname="$(get_current_os_name)" || return 1

    local download_link=null
    if [[ "$runtime" == "dotnet" ]]; then
        download_link="$azure_feed/Runtime/$specific_version/dotnet-runtime-$specific_version-$osname-$normalized_architecture.tar.gz"
    elif [[ "$runtime" == "aspnetcore" ]]; then
        download_link="$azure_feed/aspnetcore/Runtime/$specific_version/aspnetcore-runtime-$specific_version-$osname-$normalized_architecture.tar.gz"
    elif [ -z "$runtime" ]; then
        download_link="$azure_feed/Sdk/$specific_version/dotnet-sdk-$specific_version-$osname-$normalized_architecture.tar.gz"
    else
        return 1
    fi

    echo "$download_link"
    return 0
}

# args:
# azure_feed - $1
# channel - $2
# normalized_architecture - $3
# specific_version - $4
construct_legacy_download_link() {
    eval $invocation

    local azure_feed="$1"
    local channel="$2"
    local normalized_architecture="$3"
    local specific_version="${4//[$'\t\r\n']}"

    local distro_specific_osname
    distro_specific_osname="$(get_legacy_os_name)" || return 1

    local legacy_download_link=null
    if [[ "$runtime" == "dotnet" ]]; then
        legacy_download_link="$azure_feed/Runtime/$specific_version/dotnet-$distro_specific_osname-$normalized_architecture.$specific_version.tar.gz"
    elif [ -z "$runtime" ]; then
        legacy_download_link="$azure_feed/Sdk/$specific_version/dotnet-dev-$distro_specific_osname-$normalized_architecture.$specific_version.tar.gz"
    else
        return 1
    fi

    echo "$legacy_download_link"
    return 0
}

get_user_install_path() {
    eval $invocation

    if [ ! -z "${DOTNET_INSTALL_DIR:-}" ]; then
        echo "$DOTNET_INSTALL_DIR"
    else
        echo "$HOME/.dotnet"
    fi
    return 0
}

# args:
# install_dir - $1
resolve_installation_path() {
    eval $invocation

    local install_dir=$1
    if [ "$install_dir" = "<auto>" ]; then
        local user_install_path="$(get_user_install_path)"
        say_verbose "resolve_installation_path: user_install_path=$user_install_path"
        echo "$user_install_path"
        return 0
    fi

    echo "$install_dir"
    return 0
}

# args:
# install_root - $1
get_installed_version_info() {
    eval $invocation

    local install_root="$1"
    local version_file="$(combine_paths "$install_root" "$local_version_file_relative_path")"
    say_verbose "Local version file: $version_file"
    if [ ! -z "$version_file" ] | [ -r "$version_file" ]; then
        local version_info="$(cat "$version_file")"
        echo "$version_info"
        return 0
    fi

    say_verbose "Local version file not found."
    return 0
}

# args:
# relative_or_absolute_path - $1
get_absolute_path() {
    eval $invocation

    local relative_or_absolute_path=$1
    echo "$(cd "$(dirname "$1")" && pwd -P)/$(basename "$1")"
    return 0
}

# args:
# input_files - stdin
# root_path - $1
# out_path - $2
# override - $3
copy_files_or_dirs_from_list() {
    eval $invocation

    local root_path="$(remove_trailing_slash "$1")"
    local out_path="$(remove_trailing_slash "$2")"
    local override="$3"
    local osname="$(get_current_os_name)"
    local override_switch=$(
        if [ "$override" = false ]; then
            if [[ "$osname" == "linux-musl" ]]; then
                printf -- "-u";
            else
                printf -- "-n";
            fi
        fi)

    cat | uniq | while read -r file_path; do
        local path="$(remove_beginning_slash "${file_path#$root_path}")"
        local target="$out_path/$path"
        if [ "$override" = true ] || (! ([ -d "$target" ] || [ -e "$target" ])); then
            mkdir -p "$out_path/$(dirname "$path")"
            cp -R $override_switch "$root_path/$path" "$target"
        fi
    done
}

# args:
# zip_path - $1
# out_path - $2
extract_dotnet_package() {
    eval $invocation

    local zip_path="$1"
    local out_path="$2"

    local temp_out_path="$(mktemp -d "$temporary_file_template")"

    local failed=false
    tar -xzf "$zip_path" -C "$temp_out_path" > /dev/null || failed=true

    local folders_with_version_regex='^.*/[0-9]+\.[0-9]+[^/]+/'
    find "$temp_out_path" -type f | grep -Eo "$folders_with_version_regex" | sort | copy_files_or_dirs_from_list "$temp_out_path" "$out_path" false
    find "$temp_out_path" -type f | grep -Ev "$folders_with_version_regex" | copy_files_or_dirs_from_list "$temp_out_path" "$out_path" "$override_non_versioned_files"

    rm -rf "$temp_out_path"

    if [ "$failed" = true ]; then
        say_err "Extraction failed"
        return 1
    fi
}

# args:
# remote_path - $1
# [out_path] - $2 - stdout if not provided
download() {
    eval $invocation

    local remote_path="$1"
    local out_path="${2:-}"

    local failed=false
    if machine_has "curl"; then
        downloadcurl "$remote_path" "$out_path" || failed=true
    elif machine_has "wget"; then
        downloadwget "$remote_path" "$out_path" || failed=true
    else
        failed=true
    fi
    if [ "$failed" = true ]; then
        say_verbose "Download failed: $remote_path"
        return 1
    fi
    return 0
}

downloadcurl() {
    eval $invocation
    local remote_path="$1"
    local out_path="${2:-}"

    # Append feed_credential as late as possible before calling curl to avoid logging feed_credential
    remote_path="${remote_path}${feed_credential}"

    local failed=false
    if [ -z "$out_path" ]; then
        curl --retry 10 -sSL -f --create-dirs "$remote_path" || failed=true
    else
        curl --retry 10 -sSL -f --create-dirs -o "$out_path" "$remote_path" || failed=true
    fi
    if [ "$failed" = true ]; then
        say_verbose "Curl download failed"
        return 1
    fi
    return 0
}

downloadwget() {
    eval $invocation
    local remote_path="$1"
    local out_path="${2:-}"

    # Append feed_credential as late as possible before calling wget to avoid logging feed_credential
    remote_path="${remote_path}${feed_credential}"

    local failed=false
    if [ -z "$out_path" ]; then
        wget -q --tries 10 -O - "$remote_path" || failed=true
    else
        wget --tries 10 -O "$out_path" "$remote_path" || failed=true
    fi
    if [ "$failed" = true ]; then
        say_verbose "Wget download failed"
        return 1
    fi
    return 0
}

calculate_vars() {
    eval $invocation
    valid_legacy_download_link=true

    normalized_architecture="$(get_normalized_architecture_from_architecture "$architecture")"
    say_verbose "normalized_architecture=$normalized_architecture"

    specific_version="$(get_specific_version_from_version "$azure_feed" "$channel" "$normalized_architecture" "$version")"
    say_verbose "specific_version=$specific_version"
    if [ -z "$specific_version" ]; then
        say_err "Could not get version information."
        return 1
    fi

    download_link="$(construct_download_link "$azure_feed" "$channel" "$normalized_architecture" "$specific_version")"
    say_verbose "download_link=$download_link"

    legacy_download_link="$(construct_legacy_download_link "$azure_feed" "$channel" "$normalized_architecture" "$specific_version")" || valid_legacy_download_link=false

    if [ "$valid_legacy_download_link" = true ]; then
        say_verbose "legacy_download_link=$legacy_download_link"
    else
        say_verbose "Cound not construct a legacy_download_link; omitting..."
    fi

    install_root="$(resolve_installation_path "$install_dir")"
    say_verbose "install_root=$install_root"
}

install_dotnet() {
    eval $invocation
    local download_failed=false
    local asset_name=''
    local asset_relative_path=''

    if [[ "$runtime" == "dotnet" ]]; then
        asset_relative_path="shared/Microsoft.NETCore.App"
        asset_name=".NET Core Runtime"
    elif [[ "$runtime" == "aspnetcore" ]]; then
        asset_relative_path="shared/Microsoft.AspNetCore.App"
        asset_name="ASP.NET Core Runtime"
    elif [ -z "$runtime" ]; then
        asset_relative_path="sdk"
        asset_name=".NET Core SDK"
    else
        say_err "Invalid value for \$runtime"
        return 1
    fi

    #  Check if the SDK version is already installed.
    if is_dotnet_package_installed "$install_root" "$asset_relative_path" "$specific_version"; then
        say "$asset_name version $specific_version is already installed."
        return 0
    fi

    mkdir -p "$install_root"
    zip_path="$(mktemp "$temporary_file_template")"
    say_verbose "Zip path: $zip_path"

    say "Downloading link: $download_link"

    # Failures are normal in the non-legacy case for ultimately legacy downloads.
    # Do not output to stderr, since output to stderr is considered an error.
    download "$download_link" "$zip_path" 2>&1 || download_failed=true

    #  if the download fails, download the legacy_download_link
    if [ "$download_failed" = true ]; then
        say "Cannot download: $download_link"

        if [ "$valid_legacy_download_link" = true ]; then
            download_failed=false
            download_link="$legacy_download_link"
            zip_path="$(mktemp "$temporary_file_template")"
            say_verbose "Legacy zip path: $zip_path"
            say "Downloading legacy link: $download_link"
            download "$download_link" "$zip_path" 2>&1 || download_failed=true
        fi
    fi

    if [ "$download_failed" = true ]; then
        say_err "Could not download $asset_name version $specific_version"
        return 1
    fi

    say "Extracting zip from $download_link"
    extract_dotnet_package "$zip_path" "$install_root"

    #  Check if the SDK version is now installed; if not, fail the installation.
    if ! is_dotnet_package_installed "$install_root" "$asset_relative_path" "$specific_version"; then
        say_err "$asset_name version $specific_version failed to install with an unknown error."
        return 1
    fi

    return 0
}

local_version_file_relative_path="/.version"
bin_folder_relative_path=""
temporary_file_template="${TMPDIR:-/tmp}/dotnet.XXXXXXXXX"

channel="LTS"
version="Latest"
install_dir="<auto>"
architecture="<auto>"
dry_run=false
no_path=false
azure_feed="https://builds.dotnet.microsoft.com/dotnet"
uncached_feed="https://dotnetcli.blob.core.windows.net/dotnet"
feed_credential=""
verbose=false
runtime=""
runtime_id=""
override_non_versioned_files=true

while [ $# -ne 0 ]
do
    name="$1"
    case "$name" in
        -c|--channel|-[Cc]hannel)
            shift
            channel="$1"
            ;;
        -v|--version|-[Vv]ersion)
            shift
            version="$1"
            ;;
        -i|--install-dir|-[Ii]nstall[Dd]ir)
            shift
            install_dir="$1"
            ;;
        --arch|--architecture|-[Aa]rch|-[Aa]rchitecture)
            shift
            architecture="$1"
            ;;
        --shared-runtime|-[Ss]hared[Rr]untime)
            say_warning "The --shared-runtime flag is obsolete and may be removed in a future version of this script. The recommended usage is to specify '--runtime dotnet'."
            if [ -z "$runtime" ]; then
                runtime="dotnet"
            fi
            ;;
        --runtime|-[Rr]untime)
            shift
            runtime="$1"
            if [[ "$runtime" != "dotnet" ]] && [[ "$runtime" != "aspnetcore" ]]; then
                say_err "Unsupported value for --runtime: '$1'. Valid values are 'dotnet' and 'aspnetcore'."
                exit 1
            fi
            ;;
        --dry-run|-[Dd]ry[Rr]un)
            dry_run=true
            ;;
        --no-path|-[Nn]o[Pp]ath)
            no_path=true
            ;;
        --verbose|-[Vv]erbose)
            verbose=true
            ;;
        --azure-feed|-[Aa]zure[Ff]eed)
            shift
            azure_feed="$1"
            ;;
        --uncached-feed|-[Uu]ncached[Ff]eed)
            shift
            uncached_feed="$1"
            ;;
        --feed-credential|-[Ff]eed[Cc]redential)
            shift
            feed_credential="$1"
            ;;
        --runtime-id|-[Rr]untime[Ii]d)
            shift
            runtime_id="$1"
            ;;
        --skip-non-versioned-files|-[Ss]kip[Nn]on[Vv]ersioned[Ff]iles)
            override_non_versioned_files=false
            ;;
        -?|--?|-h|--help|-[Hh]elp)
            script_name="$(basename "$0")"
            echo ".NET Tools Installer"
            echo "Usage: $script_name [-c|--channel <CHANNEL>] [-v|--version <VERSION>] [-p|--prefix <DESTINATION>]"
            echo "       $script_name -h|-?|--help"
            echo ""
            echo "$script_name is a simple command line interface for obtaining dotnet cli."
            echo ""
            echo "Options:"
            echo "  -c,--channel <CHANNEL>         Download from the channel specified, Defaults to \`$channel\`."
            echo "      -Channel"
            echo "          Possible values:"
            echo "          - Current - most current release"
            echo "          - LTS - most current supported release"
            echo "          - 2-part version in a format A.B - represents a specific release"
            echo "              examples: 2.0; 1.0"
            echo "          - Branch name"
            echo "              examples: release/2.0.0; Master"
            echo "  -v,--version <VERSION>         Use specific VERSION, Defaults to \`$version\`."
            echo "      -Version"
            echo "          Possible values:"
            echo "          - latest - most latest build on specific channel"
            echo "          - coherent - most latest coherent build on specific channel"
            echo "              coherent applies only to SDK downloads"
            echo "          - 3-part version in a format A.B.C - represents specific version of build"
            echo "              examples: 2.0.0-preview2-006120; 1.1.0"
            echo "  -i,--install-dir <DIR>             Install under specified location (see Install Location below)"
            echo "      -InstallDir"
            echo "  --architecture <ARCHITECTURE>      Architecture of dotnet binaries to be installed, Defaults to \`$architecture\`."
            echo "      --arch,-Architecture,-Arch"
            echo "          Possible values: x64, arm, and arm64"
            echo "  --runtime <RUNTIME>                Installs a shared runtime only, without the SDK."
            echo "      -Runtime"
            echo "          Possible values:"
            echo "          - dotnet     - the Microsoft.NETCore.App shared runtime"
            echo "          - aspnetcore - the Microsoft.AspNetCore.App shared runtime"
            echo "  --skip-non-versioned-files         Skips non-versioned files if they already exist, such as the dotnet executable."
            echo "      -SkipNonVersionedFiles"
            echo "  --dry-run,-DryRun                  Do not perform installation. Display download link."
            echo "  --no-path, -NoPath                 Do not set PATH for the current process."
            echo "  --verbose,-Verbose                 Display diagnostics information."
            echo "  --azure-feed,-AzureFeed            Azure feed location. Defaults to $azure_feed, This parameter typically is not changed by the user."
            echo "  --uncached-feed,-UncachedFeed      Uncached feed location. This parameter typically is not changed by the user."
            echo "  --feed-credential,-FeedCredential  Azure feed shared access token. This parameter typically is not specified."
            echo "  --runtime-id                       Installs the .NET Tools for the given platform (use linux-x64 for portable linux)."
            echo "      -RuntimeId"
            echo "  -?,--?,-h,--help,-Help             Shows this help message"
            echo ""
            echo "Obsolete parameters:"
            echo "  --shared-runtime                   The recommended alternative is '--runtime dotnet'."
            echo "      -SharedRuntime                 Installs just the shared runtime bits, not the entire SDK."
            echo ""
            echo "Install Location:"
            echo "  Location is chosen in following order:"
            echo "    - --install-dir option"
            echo "    - Environmental variable DOTNET_INSTALL_DIR"
            echo "    - $HOME/.dotnet"
            exit 0
            ;;
        *)
            say_err "Unknown argument \`$name\`"
            exit 1
            ;;
    esac

    shift
done

check_min_reqs
calculate_vars

if [ "$dry_run" = true ]; then
    say "Payload URL: $download_link"
    if [ "$valid_legacy_download_link" = true ]; then
        say "Legacy payload URL: $legacy_download_link"
    fi
    say "Repeatable invocation: ./$(basename "$0") --version $specific_version --channel $channel --install-dir $install_dir"
    exit 0
fi

check_pre_reqs
install_dotnet

bin_path="$(get_absolute_path "$(combine_paths "$install_root" "$bin_folder_relative_path")")"
if [ "$no_path" = false ]; then
    say "Adding to current process PATH: \`$bin_path\`. Note: This change will be visible only when sourcing script."
    export PATH="$bin_path":"$PATH"
else
    say "Binaries of dotnet can be found in $bin_path"
fi

say "Installation finished successfully."