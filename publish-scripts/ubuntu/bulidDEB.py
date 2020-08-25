#! /usr/bin/env python3.6
import os
import wget
import zipfile
import shutil
import datetime
from string import Template
from shared import constants
from shared import helper

def returnDebVersion(version):
    # version used in url is provided from user input
    # version used for packaging .deb package needs a slight modification
    # for beta, change to tilde, so it will be placed before rtm versions in apt
    # https://unix.stackexchange.com/questions/230911/what-is-the-meaning-of-the-tilde-in-some-debian-openjdk-package-version-string/230921
    strlist = version.split('-')
    if len(strlist) == 1:
        return strlist[0]+"-1"
    elif len(strlist) == 2:
        return f"{strlist[0]}~{strlist[1]}-1"
    else:
        raise NotImplementedError

# output a deb package
# depends on gzip, dpkg-deb, strip
@helper.restoreDirectory
def preparePackage(packageName):
    os.chdir(constants.DRIVERROOTDIR)

    debianVersion = returnDebVersion(constants.VERSION)
    packageFolder = f"{packageName}_{debianVersion}"
    buildFolder = os.path.join(os.getcwd(), constants.BUILDFOLDER, packageFolder)
    helper.linuxOutput(buildFolder, packageName)

    os.chdir(buildFolder)
    document = os.path.join("usr", "share", "doc", packageName)
    os.makedirs(document)
    # write copywrite
    print("include MIT copyright")
    scriptDir = os.path.abspath(os.path.dirname(__file__))
    shutil.copyfile(os.path.join(scriptDir, "copyright"), os.path.join(document, "copyright"))
    # write changelog
    with open(os.path.join(scriptDir, "changelog_template")) as f:
        stringData = f.read()  # read until EOF
    t = Template(stringData)
    # datetime example: Tue, 06 April 2018 16:32:31
    time = datetime.datetime.utcnow().strftime("%a, %d %b %Y %X")
    with open(os.path.join(document, "changelog.Debian"), "w") as f:
        print(f"writing changelog with date utc: {time}")
        f.write(t.safe_substitute(DEBIANVERSION=debianVersion, DATETIME=time, VERSION=constants.VERSION, PACKAGENAME=packageName))
    # by default gzip compress file in place
    output = helper.printReturnOutput(["gzip", "-9", "-n", os.path.join(document, "changelog.Debian")])
    helper.chmodFolderAndFiles(os.path.join("usr", "share"))

    debian = "DEBIAN"
    os.makedirs(debian)
    # get all files under usr/ and produce a md5 hash
    print("trying to produce md5 hashes")
    with open('DEBIAN/md5sums', 'w') as md5file:
        # iterate over all files under usr/
        # get their md5sum
        for dirpath, _, filenames in os.walk('usr'):
            for f in filenames:
                filepath = os.path.join(dirpath, f)
                if not os.path.islink(filepath):
                    h = helper.produceHashForfile(filepath, 'md5', Upper=False)
                    md5file.write(f"{h}  {filepath}\n")

    # produce the control file from template
    deps = []
    for key, value in constants.LINUXDEPS.items():
        entry = f"{key} ({value})"
        deps.append(entry)
    deps = ','.join(deps)
    with open(os.path.join(scriptDir, "control_template")) as f:
        stringData = f.read()
    t = Template(stringData)
    with open(os.path.join(debian, "control"), "w") as f:
        print("trying to write control file")
        f.write(t.safe_substitute(DEBIANVERSION=debianVersion, PACKAGENAME=packageName, DEPENDENCY=deps))
    helper.chmodFolderAndFiles(debian)

    postinst = ''
    with open(os.path.join(scriptDir, "postinst_template")) as f:
        postinst = f.read()
    with open(os.path.join(debian, "postinst"), "w") as f:
        print("trying to write postinst file")
        f.write(postinst)

    # postinstall has to be 0755 in order for it to work.
    os.chmod(os.path.join(debian, "postinst"), 0o755)

    os.chdir(constants.DRIVERROOTDIR)
    output = helper.printReturnOutput(["fakeroot", "dpkg-deb", "--build",
                   os.path.join(constants.BUILDFOLDER, packageFolder), os.path.join(constants.ARTIFACTFOLDER, packageFolder+".deb")])
    assert(f"building package '{packageName}'" in output)
