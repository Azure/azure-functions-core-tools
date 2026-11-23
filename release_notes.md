# Azure Functions CLI

#### Host Version

#### Changes
- Fixed `func kubernetes deploy` not completing for LoadBalancer services whose ingress reports only a hostname (such as Docker Desktop). The command now falls back to the ingress hostname when no IP is set, and keeps polling until an ingress entry has an address. (#3215)
