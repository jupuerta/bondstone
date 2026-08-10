# arch-forge-backend

ASP.NET Core 8 API for ArchForge, implementing `docs/ArchForge_Technical_Design_V1.md` §3, §5 and §6.
Monolith split into layers (Clean Architecture, dependencies pointing inwards), no CQRS framework.

```
Api  ──▶  Application  ──▶  Domain
 │                              ▲
 └──────▶  Infrastructure ──────┘
```

- `Domain` — entities and enums, no dependencies.
- `Application` — commands/queries as plain classes with a `Handle()` method, the analyzer, the
  per-`NodeType` configuration schemas, and the repository interface.
- `Infrastructure` — EF Core + Npgsql, Fluent API configurations, migrations, repository.
- `Api` — one controller, DI composition, exception→status mapping.

## Running it

Postgres plus the API, from this folder:

```bash
docker compose up -d --build     # api on http://localhost:8080
```

For the whole stack including the frontend, use `../docker-compose.yml` instead.

Against a local Postgres, without containerising the API:

```bash
docker compose up -d postgres
dotnet run --project src/arch-forge-backend.Api   # http://localhost:5012, Swagger at /swagger
```

Migrations are applied on startup (`Database.Migrate()` in `Program.cs`), so there is no separate
migration step — justified by V1's scale (§3.4).

## Tests

```bash
dotnet test
```

| Project | Type | Covers |
|---|---|---|
| `arch-forge-backend.Domain.Tests` | Unit | Entity defaults the other layers rely on (version starts at 1, `Configuration` is `{}`) |
| `arch-forge-backend.Application.Tests` | Unit | `SpofRule`, `SyncDependencyChainRule`, score formula and clamp, commands against a fake repository (409 path, schema validation) |
| `arch-forge-backend.Api.IntegrationTests` | Integration | `POST → GET → PUT (incl. 409) → POST /analyze` against a real Postgres via Testcontainers |

The integration tests need a running Docker daemon.

## API

| Endpoint | Notes |
|---|---|
| `POST /api/architectures` | Creates an empty architecture, returns `id`, `shareToken`, `version: 1` |
| `GET /api/architectures/{shareToken}` | Full architecture, `404` if the token is unknown |
| `PUT /api/architectures/{id}` | Full snapshot + expected `version`; `409` if stale, `400` if a node's `configuration` fails its schema |
| `POST /api/architectures/{id}/analyze` | Runs the rules, returns `score` + `findings` |

No authentication in V1 — a `shareToken` is the only handle on an architecture (§3.5).

### Contract details worth knowing

- Enums (`NodeType`, `ProtocolType`, `Severity`) serialize as **strings**, not numbers
  (`JsonStringEnumConverter` in `Program.cs`).
- `NodeDto.position` is a **nested object** (`{ "x": 0, "y": 0 }`), not flat `positionX`/`positionY`.
- `NodeDto.configuration` is a **JSON object**, not a string. `Nodes.Configuration` is `jsonb NOT NULL`,
  so an unconfigured node round-trips as `{}`.
- The `PUT` replaces the whole node/connection set in one transaction; it does not diff (§3.6).

## Concurrency

`Architecture.Version` is an EF concurrency token, so the generated `UPDATE` carries
`WHERE "Id" = @id AND "Version" = @originalVersion`. Zero rows affected → `409 Conflict` with the
current version in the body; one row → version incremented and returned (§3.7).

## Deployment

Behind a reverse proxy that terminates TLS (§9). The container serves plain HTTP on 8080:
HTTPS redirection is enabled only in Development, and `UseForwardedHeaders` reads
`X-Forwarded-Proto`/`X-Forwarded-For` from the proxy. CI (`.github/workflows/ci.yml`) runs
`dotnet build` + `dotnet test` and verifies the production image builds on every push.
