# Azure Functions CLI (next release)

#### Changes

- Fixed `func kubernetes deploy` failing with `Invalid property identifier character: {` for dotnet-isolated projects with more than one function. The `print-functions.sh` script previously used a `sed` command that only captured the last function name and produced malformed JSON; replaced with `awk` to correctly key each function object by its `name` field. (#3585)
