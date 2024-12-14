#! /bin/bash

# this is what our hosting environment does; we need to validate we can run the exe when mounted like this
fuse-zip ./ZippedOnWindows.zip ./mnt -r

# print out directory for debugging
cd ./mnt
ls -l

# run the exe to make sure it works
./ZippedExe