# Application layer

Use cases and orchestration: command/query handlers, application services, DTOs, and the
interfaces that `Infrastructure` implements.

Empty as of F-01 (`persistence-foundation`) — the folder exists to establish the layering
before the first slice needs it. F-02 onward fill it in, one subfolder per bounded context
(membership, scheduling, training, notifications).

**Layering rule** (convention, not compiler-enforced — this is a single project):

| Layer | May reference |
| --- | --- |
| `Domain` | nothing |
| `Application` | `Domain` |
| `Infrastructure` | `Domain`, `Application` — and it is the **only** layer that may reference EF Core |

If this boundary starts to rot, the escalation is splitting into separate `.csproj` projects
so the compiler enforces it. That was deliberately deferred — see the plan's "What We're NOT Doing".
