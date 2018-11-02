#! /bin/bash

# Exit on errors
set -e

cd /home/site/wwwroot
if [ -d worker_venv ]; then
    rm -rf worker_venv
fi
python -m venv --copies worker_venv
source worker_venv/bin/activate
pip install -r /requirements.txt
apt-get update
apt-get install zip -y
zip --symlinks -r /app.zip .