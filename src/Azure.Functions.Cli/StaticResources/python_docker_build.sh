#! /bin/bash

# Exit on errors
set -e

cd /home/site/wwwroot
if [ -d worker_venv ]; then
    rm -rf worker_venv
fi

python -m venv --copies worker_venv
worker_venv/bin/pip install -r requirements.txt

# Bundle using pyinstaller
worker_venv/bin/pip install pyinstaller==3.4

if [ -f /worker_packages.txt ]; then
	pip install --target=/worker_packages -r /worker_packages.txt
fi

pyinstaller_success=false

# If pyinstaller succeeds, we deactivate and remove the venv
if worker_venv/bin/python /python_bundle_script.py /azure-functions-host/workers/python/worker.py /worker_packages; then
	pyinstaller_success=true
else
	if [ -d ./worker-bundle ]; then
		rm -r ./worker-bundle
	fi
fi

if [ -d ./build ]; then
	rm -r ./build
fi

if [ -f worker-bundle.spec ]; then
	rm worker-bundle.spec
fi

apt-get update
apt-get install zip -y

if [ "$pyinstaller_success" = true ]; then
	zip --symlinks -r /app.zip . -x "worker_venv/*"
else
	zip --symlinks -r /app.zip .
fi