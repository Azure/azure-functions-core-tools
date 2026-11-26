# Azure Functions CLI

#### Host Version

- Host Runtime Version: 4.1055.300
- In-Proc CLI:
  - CLI Version: 4.9.0
  - Host Runtime Version: 4.52.200 (includes 4.852.200, 4.652.200)

#### Breaking Changes

- **Python 3.9 is no longer supported.** This version has reached end-of-life. Please upgrade to Python 3.10 or later to continue using Azure Functions Core Tools.

#### Changes
- `func new` now proposes the next free default function name (for example `HttpTrigger1`) when a function folder with the default name already exists, instead of asking whether to overwrite it. (#3262)
- Fixed `func kubernetes deploy` not completing for LoadBalancer services whose ingress reports only a hostname (such as Docker Desktop). The command now falls back to the ingress hostname when no IP is set, and keeps polling until an ingress entry has an address. (#3215)
