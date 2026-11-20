# Azure Functions CLI 4.17.0

#### Host Version

- Host Runtime Version: 4.1055.300
- In-Proc CLI:
  - CLI Version: 4.9.0
  - Host Runtime Version: 4.52.200 (includes 4.852.200, 4.652.200)

#### Changes
- Fixed `func kubernetes deploy` not completing for LoadBalancer services whose ingress reports only a hostname (such as Docker Desktop). The command now falls back to the ingress hostname when no IP is set, and keeps polling until an ingress entry has an address. (#3215)
