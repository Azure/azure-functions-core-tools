#! /usr/bin/env python3.6

# depends on chocolaty
import os
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
def preparePackage(packageName):
    fileName_x86 = f"Azure.Functions.Cli.win-x86.{constants.VERSION}.zip"
    fileName_x64 = f"Azure.Functions.Cli.win-x64.{constants.VERSION}.zip"
    url_x86 = f'https://functionscdn.azureedge.net/public/{constants.VERSION}/{fileName_x86}'
    url_x64 = f'https://functionscdn.azureedge.net/public/{constants.VERSION}/{fileName_x64}'

    # version used in url is provided from user input
    # version used for packaging nuget packages needs a slight modification
    chocoVersion = getChocoVersion(constants.VERSION)

    # download the zip
    # output to local folder
    #  -- For 32 bit
    if not os.path.exists(fileName_x86):
        print(f"downloading from {url_x86}")
        wget.download(url_x86)
    #  -- For 64 bit
    if not os.path.exists(fileName_x64):
        print(f"downloading from {url_x64}")
        wget.download(url_x64)

    # get the checksums
    fileHash_x86 = produceHashForfile(fileName_x86, HASH)
    fileHash_x64 = produceHashForfile(fileName_x64, HASH)

    tools = os.path.join(constants.BUILDFOLDER, "tools")
    os.makedirs(tools)

    # write install powershell script
    scriptDir = os.path.abspath(os.path.dirname(__file__))
    with open(os.path.join(scriptDir, "installps_template")) as f:
        # TODO stream replace instead of reading the entire string into memory
        stringData = f.read()
    t = Template(stringData)
    with open(os.path.join(tools, "chocolateyinstall.ps1"), "w") as f:
        print("writing install powershell script")
        f.write(t.safe_substitute(ZIPURL_X86=url_x86, ZIPURL_X64=url_x64, PACKAGENAME=packageName,
                                  CHECKSUM_X86=fileHash_x86, CHECKSUM_X64=fileHash_x64, HASHALG=HASH))

    # write nuspec package metadata
    with open(os.path.join(scriptDir,"nuspec_template")) as f:
        stringData = f.read()
    t = Template(stringData)
    nuspecFile = os.path.join(constants.BUILDFOLDER, packageName+".nuspec")
    with open(nuspecFile,'w') as f:
        print("writing nuspec")
        f.write(t.safe_substitute(PACKAGENAME=packageName, CHOCOVERSION=chocoVersion))

    # run choco pack, stdout is merged into python interpreter stdout
    output = printReturnOutput(["choco", "pack", nuspecFile, "--outputdirectory", constants.ARTIFACTFOLDER])
    assert("Successfully created package" in output)

# FIXME why does this line not work when import module from sibling package
if __name__ == "__main__":
    # preparePackage(*sys.argv[1:])
    pass