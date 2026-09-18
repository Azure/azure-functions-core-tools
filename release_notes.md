# Azure Functions CLI 4.14.0

#### Host Version

- Host Runtime Version: 4.1052.200
- In-Proc CLI:
  - CLI Version: 4.7.0
  - Host Runtime Version: 4.51.100 (includes 4.851.100, 4.651.100)

#### Changes
- Fixed Python native dependency builds to inherit authenticated pip indexes in Docker containers (#5574)
- Make host.json optional across func publish, and pack (#5488)
- Fixed `func kubernetes deploy` failing with `Invalid property identifier character: {` for dotnet-isolated projects with more than one function. The `print-functions.sh` script previously used a `sed` command that only captured the last function name and produced malformed JSON; replaced with `awk` to correctly key each function object by its `name` field. (#3585)
