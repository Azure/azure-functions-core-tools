#! /usr/bin/env python3
import os
import wget
import zipfile
import shutil
import datetime
from string import Template
from shared import constants
from shared import helper

# version used in url is provided from user input
# version used for packaging .deb package needs a slight modification
# for beta, change to tilde, so it will be placed before rtm versions in apt
# https://unix.stackexchange.com/questions/230911/what-is-the-meaning-of-the-tilde-in-some-debian-openjdk-package-version-string/230921
def returnDebVersion(version):
    """
    Convert user-provided version into Debian package format.
    Uses tilde (~) to ensure beta versions are correctly sorted before RTM versions.
    """
    strlist = version.split('-')
    if len(strlist) == 1:
        return strlist[0]+"-1"
    elif len(strlist) == 2:
        return f"{strlist[0]}~{strlist[1]}-1"
    else:
        raise NotImplementedError

# depends on gzip, dpkg-deb, strip
@helper.restoreDirectory
def preparePackage():
    """
    Prepares and builds a Debian package for each supported architecture.
    """
    os.chdir(constants.DRIVERROOTDIR)

    debianVersion = returnDebVersion(constants.VERSION)
    print(f"debianVersion: {debianVersion}")

    for arch in ["x64", "arm64"]:
        print(f"\nBuilding package for linux-{arch}...\n")
        preparePackageForArch(arch, debianVersion)

def preparePackageForArch(arch, debianVersion):
    """
    Prepares and builds a Debian package.
    This includes setting up directories, copying necessary files,
    generating SHA256 hashes, and building the final .deb package.
    """
    packageFolderName = f"{constants.PACKAGENAME}_{debianVersion}_{arch}"
    buildFolder = os.path.join(os.getcwd(), constants.BUILDFOLDER, packageFolderName)
    helper.linuxOutput(buildFolder, arch)

    os.chdir(buildFolder)
    document = os.path.join("usr", "share", "doc", constants.PACKAGENAME)
    os.makedirs(document)

    # Copy MIT copyright file
    print("include MIT copyright")
    scriptDir = os.path.abspath(os.path.dirname(__file__))
    shutil.copyfile(os.path.join(scriptDir, "copyright"), os.path.join(document, "copyright"))

    # Generate changelog file from template
    with open(os.path.join(scriptDir, "changelog_template")) as f:
        stringData = f.read() # read until EOF
    t = Template(stringData)

    # datetime example: Tue, 06 April 2018 16:32:31
    time = datetime.datetime.utcnow().strftime("%a, %d %b %Y %X")
    with open(os.path.join(document, "changelog.Debian"), "w") as f:
        print(f"writing changelog with date utc: {time}")
        f.write(t.safe_substitute(DEBIANVERSION=debianVersion, DATETIME=time, VERSION=constants.VERSION, PACKAGENAME=constants.PACKAGENAME))

    # Compress changelog using gzip (by default gzip compress file in place)
    helper.printReturnOutput(["gzip", "-9", "-n", os.path.join(document, "changelog.Debian")])
    helper.chmodFolderAndFiles(os.path.join("usr", "share"))

    debian = "DEBIAN"
    os.makedirs(debian)

    # Generate SHA256 hashes for all files in 'usr/'
    print("trying to produce sha256 hashes")
    with open('DEBIAN/sha256sums', 'w') as sha256file:
        # iterate over all files under 'usr/' & get their sha256sum
        for dirpath, _, filenames in os.walk('usr'):
            for f in filenames:
                filepath = os.path.join(dirpath, f)
                if not os.path.islink(filepath):
                    h = helper.produceHashForfile(filepath, 'sha256', Upper=False)
                    sha256file.write(f"{h}  {filepath}\n")

    # Generate the control file with package dependencies from template
    deps = []
    for key, value in constants.LINUXDEPS.items():
        entry = f"{key} ({value})"
        deps.append(entry)
    deps = ','.join(deps)
    with open(os.path.join(scriptDir, "control_template")) as f:
        stringData = f.read()
    t = Template(stringData)
    with open(os.path.join(debian, "control"), "w") as f:
        if arch == "x64":
            arch = "amd64"
        print("trying to write control file - arch:", arch)
        f.write(t.safe_substitute(DEBIANVERSION=debianVersion, PACKAGENAME=constants.PACKAGENAME, DEPENDENCY=deps, ARCH=arch))
    helper.chmodFolderAndFiles(debian)

    # Generate post-install script
    postinst = ''
    with open(os.path.join(scriptDir, "postinst_template")) as f:
        postinst = f.read()
    with open(os.path.join(debian, "postinst"), "w") as f:
        print("trying to write postinst file")
        f.write(postinst)

    # Ensure post-install script has correct permissions
    # postinstall has to be 0755 in order for it to work.
    os.chmod(os.path.join(debian, "postinst"), 0o755)

    # Build the Debian package using dpkg-deb
    os.chdir(constants.DRIVERROOTDIR)
    output = helper.printReturnOutput(["fakeroot", "dpkg-deb", "--build", "-Zxz",
                   os.path.join(constants.BUILDFOLDER, packageFolderName), os.path.join(constants.ARTIFACTFOLDER, packageFolderName+".deb")])
    assert(f"building package '{constants.PACKAGENAME}'" in output)
