---
type: experience
role: Senior Software Engineer
organization: Nimbus Systems
location: Seattle, WA
startDate: 2022-03
endDate: present
tech: [C#, .NET, PostgreSQL, Kubernetes, Kafka]
tags: [backend, distributed-systems, performance]
---

- Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a bounded parallel scatter-gather over 6 downstream services.
  - Rebuilt checkout fan-out as a bounded scatter-gather, cutting p99 from 840ms to 120ms.
- Led the migration of 40+ services from .NET 6 to .NET 8, retiring 12,000 lines of compatibility shim code across the platform.
  - Migrated 40+ services to .NET 8 and deleted 12k lines of shim code in the process.
- Designed and shipped an event-sourced order pipeline on Kafka, cutting duplicate-charge incidents from 14 per month to zero over two quarters.
- Built an on-call dashboard surfacing live SLO burn rate, reducing mean time to acknowledge from 22 minutes to 4 minutes for the payments team.
  - Shipped an on-call dashboard that dropped MTTA from 22 to 4 minutes.
- Mentored 3 mid-level engineers through promotion to senior, two of whom now lead their own service teams.
- Introduced contract testing between the checkout and inventory services, catching 9 breaking changes in CI before they reached staging in the first year.
