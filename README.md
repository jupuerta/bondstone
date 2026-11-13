# Bondstone

Bondstone is a tool for **designing software architectures on a canvas and getting them
analysed automatically**. You draw the components of a system — clients, gateways, services,
databases, caches, queues, zones — wire them together, and the engine reads the diagram the way
a reviewer would: it flags single points of failure, missing fallbacks, synchronous calls that
should be able to wait, cost and latency problems, zone-level faults, and more, against a catalog
of architectural rules. It can also **simulate a fault** (take a node down and propagate the
blast radius) and ships a set of **guided challenges** that teach the ideas by having you fix a
broken design.

It is aimed at developers and students learning to reason about distributed-system design. Live
at **[bondstone.dev](https://bondstone.dev)**.

> Bondstone was previously called *ArchForge*; the earlier name still appears in some folder and
> project names in the history.

## Repository layout

This is a monorepo with two independently buildable projects:

| Folder | What it is | Stack |
| --- | --- | --- |
| [`backend/`](backend/) | The API and the analysis engine | ASP.NET Core 8, Clean Architecture, EF Core + Npgsql, PostgreSQL |
| [`frontend/`](frontend/) | The public web and the editor | React 19, Vite 6, TypeScript, Tailwind 4, react-router 7, `@xyflow/react` |

Each folder has its own `README.md` with the detail (layout, tests, design notes).

## Running the whole stack

Docker and Docker Compose are all you need. From the repository root:

```bash
cp .env.example .env
# fill POSTGRES_PASSWORD in .env — compose refuses to start while it is empty, on purpose:
#   python3 -c "import secrets,string;print(''.join(secrets.choice(string.ascii_letters+string.digits) for _ in range(32)))"

docker compose up -d --build
```

This brings up PostgreSQL, the API (migrations are applied on startup) and the frontend. Open
**http://localhost:8080**. The frontend's nginx proxies `/api` to the API service, so everything
is served from a single origin.

### Running a single part

- **Backend only** (postgres + api): see [`backend/README.md`](backend/README.md) —
  `docker compose up -d --build` from inside `backend/`, or `dotnet run` against a local Postgres.
- **Frontend only** (against the API): see [`frontend/README.md`](frontend/README.md).

## Configuration

All runtime configuration is read from a root `.env` (not committed). Start from
[`.env.example`](.env.example), which documents every variable: the database credentials, the
published frontend port, and the optional demo-architecture tokens shown on the landing page.

## License

[MIT](LICENSE).
