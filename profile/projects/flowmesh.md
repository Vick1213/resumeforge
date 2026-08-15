---
type: project
name: Flowmesh
tagline: A distributed task queue with backpressure-aware priority lanes
url: https://flowmesh.dev
repoUrl: https://github.com/jordanrivera/flowmesh
startDate: 2023-01
endDate: present
tech: [Go, Redis, gRPC]
tags: [distributed-systems, infrastructure, go]
source: github
stars: 1140
---

- Built a priority-lane scheduler that sheds low-priority work under backpressure instead of queueing it, keeping p99 enqueue latency under 8ms at 50k jobs/sec.
  - Added backpressure-aware priority lanes, holding p99 enqueue latency under 8ms at 50k jobs/sec.
- Implemented exactly-once delivery on top of Redis streams with idempotency keys, replacing a homegrown at-least-once queue used in production by 3 early adopters.
- Wrote a chaos test harness that kills brokers mid-batch, used to verify zero duplicate deliveries across 500+ CI runs.
