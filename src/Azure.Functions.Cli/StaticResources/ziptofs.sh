#! /bin/sh
set -e

unzip -n /file.zip -d /tmp/extract/
chown -R $USER:$USER /tmp/extract
chmod -R +r /tmp/extract
mksquashfs /tmp/extract /file.squashfs -noappend