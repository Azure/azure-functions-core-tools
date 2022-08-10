#! /usr/bin/env python3

# depends on chocolaty
import os
from re import sub
import wget
import sys
from string import Template
from shared import constants
from shared.helper import printReturnOutput
from shared.helper import produceHashForfile

HASH = "SHA512"
def getChocoVersion(version):
    # chocolatey do not support semantic versioning2.0.0 yet
    # https://github.com/chocolatey/choco/issues/1610
    # look for hypen, and remove any dots after
    strlist = version.split('-')
    if len(strlist) == 1:
        return strlist[0]
    elif len(strlist) == 2:
        # prerelease
        return f"{strlist[0]}-{strlist[1].replace('.','')}"
    else:
        raise NotImplementedError

# for windows, there's v1 and v2 versions
# assume buildFolder is clean
# output a deb nupkg
# depends on chocolatey
def preparePackage():
    archList = [
        "ARM64",
        "X86",
        "X64"
    ]
    substitutionMapping = {
        "PACKAGENAME": constants.PACKAGENAME,
        "HASHALG": HASH,
        "CHOCOVERSION": getChocoVersion(constants.VERSION)
    }

    tools = os.path.join(constants.BUILDFOLDER, "tools")
    os.makedirs(tools)

    for arch in archList:
        fileName = f"Azure.Functions.Cli.win-{arch.lower()}.{constants.VERSION}.zip"
        url = f'https://functionscdn.azureedge.net/public/{constants.VERSION}/{fileName}'
        substitutionMapping[f"ZIPURL_{arch}"] = url

        # download the zip
        # output to local folder
        if not os.path.exists(fileName):
            print(f"downloading from {url}")
            wget.download(url)

        # get the checksums
        fileHash = produceHashForfile(fileName, HASH)
        substitutionMapping[f"CHECKSUM_{arch}"] = fileHash

    # write install powershell script
    scriptDir = os.path.abspath(os.path.dirname(__file__))
    with open(os.path.join(scriptDir, "installps_template")) as f:
        # TODO stream replace instead of reading the entire string into memory
        stringData = f.read()
    t = Template(stringData)
    with open(os.path.join(tools, "chocolateyinstall.ps1"), "w") as f:
        print("writing install powershell script")
        f.write(t.safe_substitute(substitutionMapping))

    # write nuspec package metadata
    with open(os.path.join(scriptDir,"nuspec_template")) as f:
        stringData = f.read()
    t = Template(stringData)
    nuspecFile = os.path.join(constants.BUILDFOLDER, constants.PACKAGENAME+".nuspec")
    with open(nuspecFile, 'w') as f:
        print("writing nuspec")
        f.write(t.safe_substitute(substitutionMapping))

    # run choco pack, stdout is merged into python interpreter stdout
    output = printReturnOutput(["choco", "pack", nuspecFile, "--outputdirectory", constants.ARTIFACTFOLDER])
    assert("Successfully created package" in output)

# FIXME why does this line not work when import module from sibling package
if __name__ == "__main__":
    # preparePackage(*sys.argv[1:])
    pass