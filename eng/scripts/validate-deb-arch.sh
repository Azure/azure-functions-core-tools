#!/usr/bin/env bash
ARTIFACT_DIR="${1:-./artifact}"

echo "Validating Architecture field in .deb packages in ${ARTIFACT_DIR}"
shopt -s nullglob
debs=("${ARTIFACT_DIR}"/*.deb)

if [ ${#debs[@]} -eq 0 ]; then
  echo "No .deb packages found in ${ARTIFACT_DIR}"
  exit 1
fi

for deb in "${debs[@]}"; do
  echo
  echo "== Checking $deb =="
  arch=$(dpkg-deb -I "$deb" | grep -i '^Architecture:' || true)
  if [ -z "$arch" ]; then
    echo "ERROR: Architecture field missing in control"
    exit 1
  fi
  echo "$arch"
done

echo
echo "All .deb packages have Architecture field set."
