#! /usr/bin/env python3.6

# same for all different OSes
PACKAGENAME_BASE = "azure-functions-core-tools"
CMD = "func"
ARTIFACTFOLDER = "artifact"
BUILDFOLDER = "build"
TESTFOLDER = "test"

# to be set in driver.py
# do not use it as default argument!!
VERSION = NotImplementedError
DRIVERROOTDIR = NotImplementedError

# linux specific, for now, its ubuntu + fedora
LINUXDEPS = {}

TELEMETRY_INFO = "\n Telemetry \n --------- \n The Azure Functions Core tools collect usage data in order to help us improve your experience." \
  + "\n The data is anonymous and doesn\'t include any user specific or personal information. The data is collected by Microsoft." \
  + "\n \n You can opt-out of telemetry by setting the FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT environment variable to \'1\' or \'true\' using your favorite shell."