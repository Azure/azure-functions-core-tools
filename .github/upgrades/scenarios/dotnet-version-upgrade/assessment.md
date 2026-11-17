# Projects and dependencies analysis

This document provides a comprehensive overview of the projects and their dependencies in the context of upgrading to .NETCoreApp,Version=v11.0.

Detailed findings live alongside this file in `assessment/`. This page is the index: read it first, then open only the documents you need.

## Table of Contents

- [Executive Summary](#executive-summary)
  - [Highlevel Metrics](#highlevel-metrics)
  - [Projects Compatibility](#projects-compatibility)
  - [Package Compatibility](#package-compatibility)
  - [API Compatibility](#api-compatibility)
- [Top API Migration Challenges](#top-api-migration-challenges)
  - [Technologies and Features](#technologies-and-features)
  - [Most Frequent API Issues](#most-frequent-api-issues)
- [Detailed Reports](#detailed-reports)
  - [Projects Relationship Graph](assessment/project-graph.md)
  - [Aggregate NuGet packages details](assessment/nuget/aggregate-packages.md)
  - [Most Frequent API Issues (complete list)](assessment/api-issues/most-frequent-api-issues.md)
  - [Project Details](#project-details)

## Executive Summary

### Highlevel Metrics

| Metric | Count | Status |
| :--- | :---: | :--- |
| Total Projects | 28 | All require upgrade |
| Total NuGet Packages | 37 | 14 need upgrade |
| Total Code Files | 839 |  |
| Total Code Files with Incidents | 123 |  |
| Total Lines of Code | 103632 |  |
| Total Number of Issues | 546 |  |
| Proposed Target Framework | net11.0 |  |
| Estimated LOC to modify | 497+ | at least 0.5% of codebase |

### Projects Compatibility

| Difficulty | Projects | Percentage |
| :--- | :---: | :---: |
| 🟢 Low | 28 | 100.0% |
| ***Total Projects*** | ***28*** | ***100%*** |

🧪 **Test Coverage** — 14 of these projects are risky enough to add behavior-locking tests before upgrading, to catch regressions the upgrade may introduce. Requires the **dotnet-test** plugin. The per-project breakdown is in [the project inventory](assessment/projects/index.md).

This repository has 28 projects. The per-project compatibility table is in [the project inventory](assessment/projects/index.md).

### Package Compatibility

| Status | Count | Percentage |
| :--- | :---: | :---: |
| ✅ Compatible | 23 | 62.2% |
| ⚠️ Incompatible | 0 | 0.0% |
| 🔄 Upgrade Recommended | 14 | 37.8% |
| ***Total NuGet Packages*** | ***37*** | ***100%*** |

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 22 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 475 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 106631 |  |
| ***Total APIs Analyzed*** | ***107128*** |  |

## Top API Migration Challenges

### Technologies and Features

| Technology | Issues | Percentage | Migration Path |
| :--- | :---: | :---: | :--- |

### Most Frequent API Issues

| API | Count | Percentage | Category |
| :--- | :---: | :---: | :--- |
| T:System.Uri | 223 | 44.9% | Behavioral Change |
| T:System.Text.Json.JsonDocument | 76 | 15.3% | Behavioral Change |
| M:System.Uri.#ctor(System.String) | 37 | 7.4% | Behavioral Change |
| T:System.Net.Http.HttpContent | 30 | 6.0% | Behavioral Change |
| P:System.Diagnostics.ProcessStartInfo.Environment | 29 | 5.8% | Behavioral Change |
| M:System.IO.Path.Combine(System.ReadOnlySpan{System.String}) | 16 | 3.2% | Source Incompatible |
| P:System.Uri.AbsolutePath | 13 | 2.6% | Behavioral Change |
| M:System.IO.MemoryStream.#ctor(System.Byte[]) | 11 | 2.2% | Behavioral Change |
| M:System.IO.MemoryStream.#ctor | 9 | 1.8% | Behavioral Change |
| M:System.Uri.TryCreate(System.String,System.UriKind,System.Uri@) | 7 | 1.4% | Behavioral Change |

The table above is the top 10. See [the complete list](assessment/api-issues/most-frequent-api-issues.md) for every affected API.

## Detailed Reports

- [Projects Relationship Graph](assessment/project-graph.md)
- [Aggregate NuGet packages details](assessment/nuget/aggregate-packages.md)
- [Most Frequent API Issues (complete list)](assessment/api-issues/most-frequent-api-issues.md)

### Project Details

- [All 28 projects](assessment/projects/index.md)

### Supporting Files Inventory (added by assessment worker)

- [Supporting files, pipelines, docs, toolchain and open decisions](supporting-files-inventory.md): global.json, eng/ci YAML, .github workflows, the devcontainer, eng props, docs/AGENTS.md/skills references to .NET 10, the non-slnx `Terminal.Native.Probe.csproj`, and the `func init` default TFM (decision needed).

