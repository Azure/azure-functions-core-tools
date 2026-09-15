# Azure Functions CLI 4.15.0

#### Host Version

- Host Runtime Version: 4.1053.200
- In-Proc CLI:
  - CLI Version: 4.8.0
  - Host Runtime Version: 4.52.100 (includes 4.852.100, 4.652.100)

#### Changes
- Fixed Python native dependency builds to inherit authenticated pip indexes in Docker containers (#5574)
- Make host.json optional across func publish, and pack (#5488)
