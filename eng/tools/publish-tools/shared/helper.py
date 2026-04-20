#! /usr/bin/env python3
import os
import sys
import hashlib
import subprocess
from . import constants

def restoreDirectory(f):
    def inner(*args, **kwargs):
        currentDir = os.getcwd()
        returnV = f(*args, **kwargs)
        os.chdir(currentDir)
        return returnV
    return inner

# for some commands, returnCode means success
# for others you need to verify the output string yourself
def printReturnOutput(args, shell=False, confirm=False):
    begin = '=' * 38 +  "Running Subprocess" + "=" * 38
    print(begin)
    print(' '.join(args))
    if (confirm):
        input("This seems to be a non reversable behavior, do you still want to proceed? (Ctrl-C if you are not sure)\n")
    output = '-' * 40 + "Console Output" + "-" * 40
    print(output)
    try:
        binary = subprocess.check_output(args, shell=shell)
        if len(binary) < 1:
            string = None
        else:
            string = binary.decode('ascii')
        print(string)
        footer = "=" * 94
        print(footer)
        return string
    except subprocess.CalledProcessError:
        # rerun the command, direct output to stdout
        subprocess.call(args)
        raise

BUFFERSIZE = 1024
def produceHashForfile(filePath, hashType, Upper = True):
    # hashType is string name iof
    hashobj = hashlib.new(hashType)
    with open(filePath,'rb') as f:
       buf = f.read(BUFFERSIZE)
       while len(buf) > 0:
           hashobj.update(buf)
           buf = f.read(BUFFERSIZE)
    if Upper:
        return hashobj.hexdigest().upper()
    else:
        return hashobj.hexdigest().lower()

@restoreDirectory
def linuxOutput(buildFolder, arch):
    os.chdir(constants.DRIVERROOTDIR)

    fileName = f"Azure.Functions.Cli.linux-{arch}.{constants.VERSION}.zip"
    url = f'https://cdn.functions.azure.com/public/{constants.VERSION}/{fileName}'

    # download the zip
    import wget
    if not os.path.exists(fileName):
        print(f"downloading from {url}")
        try:
            wget.download(url)
        except Exception as e:
            print(f"\nERROR: unexpected error downloading {url}: {e}")
            sys.exit(1)

    usr = os.path.join(buildFolder, "usr")
    usrlib = os.path.join(usr, "lib")
    usrlibFunc = os.path.join(usrlib, constants.PACKAGENAME)
    os.makedirs(usrlibFunc)
    # unzip here
    import zipfile
    with zipfile.ZipFile(fileName) as f:
        print(f"extracting to {usrlibFunc}")
        f.extractall(usrlibFunc)

    # create relative symbolic link under bin directory, change mode to executable
    usrbin = os.path.join(usr, "bin")
    os.makedirs(usrbin)
    os.chdir(usrbin)
    print("create symlink for func")
    os.symlink(f"../lib/{constants.PACKAGENAME}/func", "func")
    exeFullPath = os.path.abspath("func")

    os.chdir(buildFolder)

    # strip shared objects
    import glob

    stripBinary = "strip"
    if arch == "arm64":
        stripBinary = "aarch64-linux-gnu-strip"

    sharedObjects = glob.glob("**/*.so", recursive=True)
    if sharedObjects:
        printReturnOutput([stripBinary, "--strip-unneeded"] + sharedObjects)

    print(f"change bin/func permission to 755")
    chmodFolderAndFiles(os.path.join(buildFolder, "usr"))
    os.chmod(exeFullPath, 0o755)

def chmodFolderAndFiles(folder):
    print(f"change permission of files in {folder}")
    os.chmod(folder, 0o755)
    for r, ds, fs in os.walk(folder):
        for d in ds:
            # folder permission to 755
            os.chmod(os.path.join(r, d), 0o755)
        for f in fs:
            # file permission to 644
            os.chmod(os.path.join(r, f), 0o644)
