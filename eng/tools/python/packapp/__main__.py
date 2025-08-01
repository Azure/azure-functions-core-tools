#!/usr/bin/env python3

import argparse
import os
import os.path
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
import stat

from enum import IntEnum

class Wheel:
    def __init__(self, wheel_path):
        self.wheel_path = wheel_path
        self.filename = os.path.basename(wheel_path)
        # Parse wheel filename: {distribution}-{version}(-{build tag})?-{python tag}-{abi tag}-{platform tag}.whl
        parts = self.filename[:-4].split('-')  # Remove .whl extension
        self.name = parts[0]
        self.version = parts[1]
        # Skip build tag if present (starts with digit)
        start_idx = 2
        if len(parts) > 2 and parts[2] and parts[2][0].isdigit():
            start_idx = 3
        if len(parts) >= start_idx + 3:
            self.python_tag = parts[start_idx]
            self.abi_tag = parts[start_idx + 1] 
            self.platform_tag = parts[start_idx + 2]
    
    def install(self, paths, maker):
        """Install wheel contents to specified paths"""
        with zipfile.ZipFile(self.wheel_path, 'r') as zf:
            # Extract all files
            name_ver = f'{self.name}-{self.version}'
            data_dir = f'{name_ver}.data'
            info_dir = f'{name_ver}.dist-info'
            
            for member in zf.namelist():
                if member.endswith('/'):
                    continue  # Skip directories
                    
                # Determine destination based on file location in wheel
                if member.startswith(f'{info_dir}/'):
                    # Skip dist-info for now - we don't need it for basic installation
                    continue
                elif member.startswith(f'{data_dir}/'):
                    # Handle data files
                    rel_path = member[len(f'{data_dir}/'):]
                    if rel_path.startswith('scripts/'):
                        dest_dir = paths['scripts']
                        dest_path = os.path.join(dest_dir, rel_path[8:])  # Remove 'scripts/'
                    elif rel_path.startswith('headers/'):
                        dest_dir = paths['headers']
                        dest_path = os.path.join(dest_dir, rel_path[8:])  # Remove 'headers/'
                    else:
                        dest_dir = paths['data']
                        dest_path = os.path.join(dest_dir, rel_path)
                else:
                    # Regular package files go to purelib/platlib
                    dest_dir = paths['purelib']
                    dest_path = os.path.join(dest_dir, member)
                
                # Create destination directory
                os.makedirs(os.path.dirname(dest_path), exist_ok=True)
                
                # Extract file
                with zf.open(member) as src:
                    with open(dest_path, 'wb') as dst:
                        shutil.copyfileobj(src, dst)
                
                # Make scripts executable on Unix-like systems
                if member.startswith(f'{data_dir}/scripts/') and os.name != 'nt':
                    os.chmod(dest_path, os.stat(dest_path).st_mode | stat.S_IEXEC)


class ScriptMaker:    
    def __init__(self, source_dir, target_dir):
        self.source_dir = source_dir
        self.target_dir = target_dir

_platform_map = {
    'linux': 'manylinux1_x86_64',
    'windows': 'win_amd64',
}

_wheel_file_pattern = r"""
    ^{namever}
    ((-(?P<build>\d[^-]*?))?-(?P<pyver>.+?)-(?P<abi>.+?)-(?P<plat>.+?)
    \.whl)$
"""

class ExitCode(IntEnum):
    success = 0
    general_error = 1
    native_deps_error = 4


def die(msg, exitcode=ExitCode.general_error):
    print(f'ERROR: {msg}', file=sys.stderr)
    sys.exit(int(exitcode))


def run(cmd, *, verbose=False, **kwargs):
    if verbose:
        stdout = stderr = None
    else:
        stdout = stderr = subprocess.PIPE

    print(' '.join(cmd))
    return subprocess.run(cmd, stdout=stdout, stderr=stderr, **kwargs)


def run_or_die(cmd, *, verbose=False, **kwargs):
    try:
        run(cmd, verbose=verbose, check=True, **kwargs)
    except subprocess.CalledProcessError as e:
        die(f'{cmd} failed with exit code {e.returncode}')


def main(argv=sys.argv[1:]):
    args = parse_args(argv)
    if not args.no_deps:
        find_and_build_deps(args)


def find_and_build_deps(args):
    app_path = pathlib.Path(args.path)
    req_txt = app_path / 'requirements.txt'

    if not req_txt.exists():
        die('missing requirements.txt file.  '
            'If you do not have any requirements, please pass --no-deps.')

    packages = []

    # First, we need to figure out the complete list of dependencies
    # without actually installing them.  Use straight `pip download`
    # for that.
    with tempfile.TemporaryDirectory(prefix='azureworker') as td:
        run_or_die([
           sys.executable, '-m', 'pip', 'download', '-r', str(req_txt), '--dest', td
        ], verbose=args.verbose)

        files = os.listdir(td)

        for filename in files:
            m = re.match(r'^(?P<name>.+?)-(?P<ver>.*?)-.*\.whl$', filename)
            if m:
                # This is a wheel.
                packages.append((m.group('name'), m.group('ver')))
            else:
                # This is a sdist.
                m = re.match(r'^(?P<namever>.+)(\.tar\.gz|\.tgz|\.zip)$',
                             filename)
                if m:
                    name, _, ver = m.group('namever').rpartition('-')
                    if name and ver:
                        packages.append((name, ver))

    # Now that we know all dependencies, download or build wheels
    # for them for the correct platform and Python version.
    with tempfile.TemporaryDirectory(prefix='azureworker') as td:
        for name, ver in packages:
            ensure_wheel(name, ver, args=args, dest=td)

        with tempfile.TemporaryDirectory(prefix='azureworkervenv') as venv:
            venv = pathlib.Path(venv)
            pyver = args.python_version
            python = f'python{pyver[0]}.{pyver[1]}'

            if args.platform == 'windows':
                sp = venv / 'Lib' / 'site-packages'
                headers = venv / 'Include'
                scripts = venv / 'Scripts'
                data = venv
            elif args.platform == 'linux' and python == "python3.6":
                sp = venv / 'lib' / python / 'site-packages'
                headers = venv / 'include' / 'site' / python
                scripts = venv / 'bin'
                data = venv
            elif args.platform == 'linux':
                sp = venv / 'lib' / 'site-packages'
                headers = venv / 'include' / 'site' / python
                scripts = venv / 'bin'
                data = venv
            else:
                die(f'unsupported platform: {args.platform}')

            maker = ScriptMaker(None, None)

            for filename in os.listdir(td):
                if not filename.endswith('.whl'):
                    continue

                wheel = Wheel(os.path.join(td, filename))

                paths = {
                    'prefix': venv,
                    'purelib': sp,
                    'platlib': sp,
                    'headers': headers / wheel.name,
                    'scripts': scripts,
                    'data': data
                }

                for dn in paths.values():
                    os.makedirs(dn, exist_ok=True)

                # print(paths, maker)
                wheel.install(paths, maker)

            for root, dirs, files in os.walk(venv):
                for file in files:
                    src = os.path.join(root, file)
                    rpath = app_path / args.packages_dir_name / \
                        os.path.relpath(src, venv)
                    dir_name, _ = os.path.split(rpath)
                    os.makedirs(dir_name, exist_ok=True)
                    shutil.copyfile(src, rpath)


def ensure_wheel(name, version, args, dest):
    # Determine the correct ABI tag based on Python version
    cmd = [
        sys.executable, '-m', 'pip', 'download', '--no-deps', '--only-binary', ':all:',
        '--python-version', args.python_version,
        '--implementation', 'cp',
        '--abi', f'cp{args.python_version}',
        '--dest', dest,
        f'{name}=={version}'
    ]

    pip = run(cmd)
    if pip.returncode != 0:
        # No wheel for this package for this platform or Python version.
        if not build_independent_wheel(name, version, args, dest):
            build_binary_wheel(name, version, args, dest)


def build_independent_wheel(name, version, args, dest):
    with tempfile.TemporaryDirectory(prefix='azureworker') as td:
        cmd = [
            sys.executable, '-m', 'pip', 'wheel', '--no-deps', '--no-binary', ':all:',
            '--wheel-dir', td,
            f'{name}=={version}'
        ]

        # First, try to build it as an independent wheel.
        pip = run(cmd)
        if pip.returncode != 0:
            return False

        wheel_re = _wheel_file_pattern.format(namever=f'{name}-[^-]*')

        for filename in os.listdir(td):
            m = re.match(wheel_re, filename, re.VERBOSE)
            if m:
                abi = m.group('abi')
                platform = m.group('plat')

                if abi == 'none' and platform == 'any':
                    # This is a universal wheel.
                    shutil.move(os.path.join(td, filename), dest)
                    return True

                break

        return False


def build_binary_wheel(name, version, args, dest):
    die(f'cannot install {name}-{version} dependency: binary dependencies without wheels are not supported when building locally. '
        f'Use the "--build remote" option to build dependencies on the Azure Functions build server, '
        f'or "--build-native-deps" option to automatically build and configure the dependencies using a Docker container. '
        f'More information at https://aka.ms/func-python-publish', ExitCode.native_deps_error)


def parse_args(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument('--verbose', default=False, action='store_true')
    parser.add_argument('--platform', type=str)
    parser.add_argument('--python-version', type=str)
    parser.add_argument('--no-deps', default=False, action='store_true')
    parser.add_argument('--packages-dir-name', type=str,
                        default='.python_packages',
                        help='folder to save packages in. '
                             'Default: .python_packages')
    parser.add_argument('path', type=str,
                        help='Path to a function app to pack.')

    args = parser.parse_args(argv)
    if not args.platform:
        die('missing required argument: --platform')

    if not args.python_version:
        die('missing required argument: --python-version')

    return args


if __name__ == '__main__':
    main()
