---
type: project
name: Ledgerline
tagline: A self-hosted double-entry budgeting app for people who like spreadsheets
repoUrl: https://github.com/jordanrivera/ledgerline
startDate: 2024-04
endDate: present
tech: [C#, ASP.NET Core, React, PostgreSQL]
tags: [fullstack, dotnet, personal-finance]
source: manual
---

- Modeled transactions as double-entry ledger lines instead of single balances, catching 2 categorization bugs during development that a single-balance model would have hidden.
- Built a CSV import pipeline with per-bank column mapping, supporting 4 bank export formats without hardcoding any of them.
  - Added a configurable CSV importer that handles 4 bank export formats without hardcoded mappings.
- Self-hosted the app on a single $6/month VPS for a household of two for over a year with zero downtime incidents.
