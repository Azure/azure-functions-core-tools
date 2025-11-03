#!/usr/bin/env bash
ARTIFACT_DIR="${1:-./artifact}"

echo "Validating Architecture field in .deb packages in ${ARTIFACT_DIR}"
shopt -s nullglob
debs=("${ARTIFACT_DIR}"/*.deb)

if [ ${#debs[@]} -eq 0 ]; then
  echo "No .deb packages found in ${ARTIFACT_DIR}"
  exit 1
fi

fail=0
for deb in "${debs[@]}"; do
  base="$(basename "$deb")"
  lower="$(echo "$base" | tr '[:upper:]' '[:lower:]')"
  expected=""

  if echo "$lower" | grep -q "arm64"; then
    expected="arm64"
  elif echo "$lower" | grep -Eq "x64|amd64"; then
    expected="amd64"
  else
    echo ">> Skipping (no arch in filename): $base"
    continue
  fi

  ctrl_arch="$(dpkg-deb -f "$deb" Architecture || true)"

  echo "== $base =="
  echo "  expected: $expected"
  echo "  control : ${ctrl_arch:-<missing>}"

  if [ -z "${ctrl_arch:-}" ]; then
    echo "  ERROR: Architecture field missing in control"
    fail=1
  elif [ "$ctrl_arch" != "$expected" ]; then
    echo "  ERROR: Filename arch ($expected) != control arch ($ctrl_arch)"
    fail=1
  else
    echo "  OK"
  fi
  echo
done

if [ $fail -ne 0 ]; then
  echo "Validation failed."
  exit 2
fi

echo "All filename/control architecture checks passed."
