#! /bin/bash

# Exit on errors
set -e

PYTHON_PACKAGE_PATH="/.python_packages/lib/site-packages"
if [[ "$PYTHON_VERSION" == "3.6"* ]]; then
    PYTHON_PACKAGE_PATH="/.python_packages/lib/python3.6/site-packages"
fi;

pip install --target="$PYTHON_PACKAGE_PATH" -r /requirements.txt
