#! /bin/bash

# Exit on errors
set -e

cd /home/site/wwwroot
if [ -d worker_venv ]; then
    rm -rf worker_venv
fi
conda config --set allow_softlinks False
conda env create -f environment.yml -p worker_venv
apt-get update
apt-get install zip -y
zip --symlinks -r /app.zip .