# Running the CLI with Docker

## Pre-requisites

- Docker installed and running

## Running the CLI

The default func runtime being built by the `Dockerfile` is linux x64.

- Build & start the container:
  - Either `cd docker` and `docker-compose up --build -d`
  - Or from root: `docker-compose -f docker/docker-compose.yml up --build -d`
- Start an interactive bash session: `docker exec -it func-cli bash`
- You can now use the CLI by running `func`
- You can exit the container by using `exit`
- When you want to kill the container, run: `docker-compose down`

> [!IMPORTANT]
> If you are using a Mac with Apple silicon (M1, M2, etc.), you will need to use the `--platform linux/arm64`
> flag when building the image for non-ARM RIDs. You can also set this in the docker-compose file.

### Testing Different Runtimes

For other runtimes, you can set the `TARGET_RUNTIME` via build arguments:

- `docker-compose build --build-arg TARGET_RUNTIME="win-x64"`
- `docker-compose up -d`

> You can also edit the docker-compose file, but this isn't as git friendly.

### Windows

For testing the windows builds of the Core Tools CLI, you will need to switch the docker engine to Windows containers.
You can do this by right-clicking the Docker icon in the system tray and selecting "Switch to Windows containers".

When in windows container mode, follow the same steps as above to build and run the container.

### macOS

You cannot use docker images to test the OSX builds of the Core Tools CLI, you will need to run them natively on a macOS device.

## Func CLI Runtimes

List of what func supports and what you can test via docker:

| Runtime | Supported? | Engine |
|-------- | ---------- | ------ |
| "min.win-arm64" | OK | Windows containers |
| "min.win-x86" | OK | Windows containers |
| "min.win-x64" | OK | Windows containers |
| "linux-x64" | OK | Linux containers (default) |
| "osx-x64" | Not Supported | n/a |
| "osx-arm64" | Not Supported | n/a |
| "win-x86" | OK | Windows containers |
| "win-x64" | OK | Windows containers |
| "win-arm64" | OK | Windows containers |

## Helpful Commands

- `docker ps` to see the running containers.
- `docker logs func-cli` to see the logs.
- `docker exec -it func-cli bash` to start an interactive shell in the container.