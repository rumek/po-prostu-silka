# Application layer

Use cases and orchestration: command/query handlers, application services, DTOs, and the
interfaces that `Infrastructure` implements.

Empty as of F-01 (`persistence-foundation`) — the folder exists to establish the layering
before the first slice needs it. F-02 onward fill it in, one subfolder per bounded context
(membership, scheduling, training, notifications).

**Layering rule** (enforced by the compiler — these are four separate projects):

| Project | May reference |
| --- | --- |
| `Domain` | nothing but the BCL and one Identity package |
| `Application` | `Domain` |
| `Infrastructure` | `Domain`, `Application` — and it is the **only** project that may reference EF Core |
| `Api` | `Application`, `Infrastructure` — the host |

That escalation has been taken (S-18): an EF Core `using` here fails `dotnet build` with CS0234,
so the boundary can no longer rot quietly. The one remaining hole is adding an EF-Core-bearing
`PackageReference` to `po-prostu-silka.Application.csproj`, which is a reviewable csproj diff.
