# Plans index

One row per feature folder under `.squad/plans/`. `NN` continues as a global execution sequence across all features when `naming.globalSequence` is `true` in `config.yaml`.

| Feature | Overview | NN range |
|---------|----------|----------|
| `crm-ticketing-foundation` | [00-overview.md](crm-ticketing-foundation/00-overview.md) | 01 |

## Planned next

Features expected to appear here once their scope is agreed and an intake is
written. Nothing below is planned yet — no plan file exists for any of them.

| Candidate feature | Depends on | Notes |
|---|---|---|
| `ticketing-core` | `crm-ticketing-foundation` | Ticket aggregate, status machine, priority, assignment, comments |
| `customers-crm` | `ticketing-core` | Accounts, contacts, contact↔ticket links, customer 360 |
| `auth-roles` | `crm-ticketing-foundation` | Identity, Admin/Agent/Customer roles, permission-gated UI |
| `reporting-dashboard` | `ticketing-core` | KPI tiles, status/priority breakdowns, SLA breach report |
| `persistence` | `crm-ticketing-foundation` | Data store, ORM, migrations — currently a stub |

Run `squad new-story <feature-slug>` to open one of these, then `/squad-plan` it.
| `persistence` | [00-overview.md](persistence/00-overview.md) | 02 |