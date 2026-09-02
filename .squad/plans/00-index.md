# Plans index

One row per feature folder under `.squad/plans/`. `NN` continues as a global execution sequence across all features when `naming.globalSequence` is `true` in `config.yaml`.

| Feature | Overview | NN range |
|---------|----------|----------|
| `crm-ticketing-foundation` | [00-overview.md](crm-ticketing-foundation/00-overview.md) | 01 |
| `persistence` | [00-overview.md](persistence/00-overview.md) | 02 |
| `ticketing-core` | [00-overview.md](ticketing-core/00-overview.md) | 03–04, 09 |
| `ticketing-ui` | [00-overview.md](ticketing-ui/00-overview.md) | 05, 08, 10 |
| `auth-roles` | [00-overview.md](auth-roles/00-overview.md) | 06 |
| `demo-data` | [00-overview.md](demo-data/00-overview.md) | 07 |

## Planned next

Features expected to appear here once their scope is agreed and an intake is
written. Nothing below is planned yet — no plan file exists for any of them.

| Candidate feature | Depends on | Notes |
|---|---|---|
| `customers-crm` | `ticketing-core` | Accounts, contacts, contact↔ticket links, customer 360 |
| `reporting-dashboard` | `ticketing-core` | KPI tiles, status/priority breakdowns, SLA breach report |

Run `squad new-story <feature-slug>` to open one of these, then `/squad-plan` it.
