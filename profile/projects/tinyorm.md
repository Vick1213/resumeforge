---
type: project
name: TinyOrm
tagline: A micro-ORM for .NET built on compiled expression trees instead of reflection
repoUrl: https://github.com/jordanrivera/tinyorm
startDate: 2020-08
endDate: 2021-03
tech: [C#, .NET]
tags: [dotnet, orm, performance]
source: manual
---

- Replaced reflection-based property mapping with compiled expression trees, cutting per-row materialization time by roughly 4x against 3 comparable micro-ORMs in benchmarks.
  - Switched row mapping from reflection to compiled expression trees, cutting materialization time ~4x.
- Wrote a source-level benchmark suite comparing against 3 popular micro-ORMs, published alongside the library so the performance claims are checkable.
