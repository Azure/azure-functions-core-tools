# Running the Cli with Docker

## Prerequisites

- Docker installed and running

## Running the CLI

The default is linux x64 build.

- `cd .devcontainer/run`
- `docker-compose up --build`
- `docker-compose down`

Or run from the root of the repository:

`docker-compose -f .devcontainer/run/docker-compose.win.yml up --build`
`docker-compose -f .devcontainer/run/docker-compose.linux.yml up --build`

### Windows

- Change docker to Windows engine
  - `docker -SwitchWindowsEngine`
- Override parameters for windows setup (you can also edit the `docker-compose.yml` file):
  - `docker-compose build --build-arg TARGET_RUNTIME=win-x64 --build-arg EXECUTABLE_NAME=func.exe`
  - `docker-compose up`

### Helpful Commands

You can also run detached by adding the `-d` flag:

`docker-compose -f .devcontainer/run/docker-compose.yml up -d`

- `docker ps` to see the running containers.
- `docker func-cli log` to see the logs.
- `docker run -it func-cli sh` to get a shell in the container.
