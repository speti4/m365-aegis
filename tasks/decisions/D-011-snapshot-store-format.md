---
status: proposed
date: 2026-10-30
cost: medium
sources: [wip/083-spike-snapshot-store-format-and-size.md]
---

# D-011 — The snapshot store's format, place, query path and retention

## Context

<The spike of task 083 fills this: what a nightly snapshot holds for a tenant of 620 mailboxes, the measured sizes of
the shapes weighed, the one-user query time of task 084's question over each, and the constraints: the data lives under
the home folder and never in the repo (D-005), no token or secret in a snapshot, the Core's existing reads are the
source.>

## Decision

<The shape, the folder under the home, the query path for the access view, the retention and the prune rule, the
package a later task adds if any. Proposed by the spike; the maintainer decides.>

## Not chosen

<The other shapes with their measured numbers, one line each, and why not.>
