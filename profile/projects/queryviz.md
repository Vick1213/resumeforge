---
type: project
name: QueryViz
tagline: A SQL query plan visualizer that runs entirely in the browser
url: https://queryviz.dev
repoUrl: https://github.com/jordanrivera/queryviz
startDate: 2021-11
endDate: present
tech: [TypeScript, WebAssembly, PostgreSQL]
tags: [developer-tools, typescript, databases]
source: github
stars: 612
---

- Parsed PostgreSQL EXPLAIN ANALYZE output into an interactive tree view, cutting the time spent diagnosing slow queries in code review from roughly 20 minutes to under 3.
  - Turned raw EXPLAIN ANALYZE output into an interactive tree, cutting query-review time from 20 minutes to under 3.
- Compiled a query-cost estimator to WebAssembly so the tool works fully offline with no backend, now used by 600+ GitHub stargazers.
- Added a shareable permalink format that encodes the query plan in the URL, removing the need to paste screenshots into chat threads.
