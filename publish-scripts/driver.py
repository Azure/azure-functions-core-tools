#! /usr/bin/env python3.6
import platform
import sys
import os
import shutil
from shared import constants

def main(*args):
    # assume follow semantic versioning 2.0.0
    ver = args[1]
    if not ver.startswith('2'):
        raise Exception(f"This script only builds packages for major version '2'. Instead received '{ver}'")
    constants.VERSION = ver
    constants.DRIVERROOTDIR = os.path.dirname(os.path.abspath(__file__))
    platformSystem = platform.system()
    if platformSystem == "Linux":
        d, _, __ = platform.linux_distribution()
        if d == "Ubuntu":
            import ubuntu.bulidDEB as dist
            print("Detected Ubuntu, starting to work on a deb package...")
        else:
            print(f"Does not support distribution {d} yet.")
            return
    elif platformSystem == "Windows":
        import chocolatey.buildNUPKG as dist
        print("Detected Windows, starting to work on a nupkg package...")
    else:
        print(f"Does not support platform {platformSystem} yet.")
        return

    # Build packages
    # we create two packages 'azure-functions-core-tools-2' and 'azure-functions-core-tools'

    # Build 'azure-functions-core-tools'
    initWorkingDir(constants.BUILDFOLDER, True)
    initWorkingDir(constants.ARTIFACTFOLDER)

    print(f"Building package {constants.PACKAGENAME_BASE}...")
    dist.preparePackage(constants.PACKAGENAME_BASE)

    # Build 'azure-functions-core-tools-2'
    initWorkingDir(constants.BUILDFOLDER, True)
    initWorkingDir(constants.ARTIFACTFOLDER)

    print(f"Building package {constants.PACKAGENAME_BASE}-2...")
    dist.preparePackage(f"{constants.PACKAGENAME_BASE}-2")

def initWorkingDir(dirName, clean = False):
    if clean:
        if os.path.exists(dirName):
            print(f"trying to clear {dirName}/ directory")
            shutil.rmtree(dirName)
    print(f"trying to create {dirName}/ directory")
    os.makedirs(dirName, exist_ok=True)

if __name__ == "__main__":
    # input example: 2.0.1-beta.25
    main(*sys.argv)
