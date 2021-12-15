#! /usr/bin/env python3
import distro
import platform
import sys
import os
import shutil
from shared import constants

def main(*args):
    # assume follow semantic versioning 2.0.0
    constants.VERSION = args[1]
    constants.DRIVERROOTDIR = os.path.dirname(os.path.abspath(__file__))
    platformSystem = platform.system()
    if platformSystem == "Linux":
        d = distro.id()
        if d == "ubuntu":
            import ubuntu.buildDEB as dist
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

    # at root
    initWorkingDir(constants.BUILDFOLDER, True)
    initWorkingDir(constants.ARTIFACTFOLDER)

    # build package
    print("Building package...")
    dist.preparePackage()

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
