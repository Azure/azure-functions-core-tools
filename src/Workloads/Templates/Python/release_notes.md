# Azure.Functions.Cli.Workloads.Templates.Python

## 1.0.0

- Initial scaffold of the Python templates workload (v2 programming model only). v1 (legacy) programming-model templates are not shipped — users on v1 should migrate to v2 (see Templates Workload Spec §7.1).
- Function names containing characters that are not valid Python identifiers (e.g. `HttpTrigger-Python`) are now sanitized before being emitted as `def` declarations, so generated `function_app.py` parses cleanly (#5202).
