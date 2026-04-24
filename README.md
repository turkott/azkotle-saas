# AZ KOTLE SaaS

B2B SaaS pro revizní techniky plynových kotlů v ČR — evidence zákazníků a kotlů, generování revizních zpráv dle **NV 191/2022 Sb.** a **TPG 704 01** do PDF, QR štítky pro fyzickou identifikaci kotlů.

## Stack

- **.NET 10** (Blazor United + ASP.NET Core Minimal API)
- **PostgreSQL 16** (multi-tenant přes Row-Level Security)
- **Redis 7** (session store, rate limiting)
- **Backblaze B2** (S3-compatible storage pro PDF a fotky)
- **Docker + Caddy** (produkce)

Kompletní specifikace: [docs/context.md](docs/context.md).

## Struktura

```
src/
├── AzKotle.Domain/          # Entities, value objects, domain events
├── AzKotle.Application/     # Use cases, services, DTOs, validátory
├── AzKotle.Infrastructure/  # EF Core, Redis, B2, external APIs
├── AzKotle.Web/             # Blazor United (SSR + WASM)
├── AzKotle.Web.Client/      # Blazor WASM client
├── AzKotle.Api/             # Minimal API host
└── AzKotle.Shared/          # Sdílené DTO, constants

tests/
├── AzKotle.Domain.Tests/
├── AzKotle.Application.Tests/
├── AzKotle.Api.IntegrationTests/
└── AzKotle.Web.E2ETests/    # Playwright
```

## Quickstart

**Požadavky:** .NET 10 SDK, Docker Desktop.

```bash
git clone https://github.com/turkott/azkotle-saas.git
cd azkotle-saas

# Lokální DB + Redis
docker compose up -d

# Build
dotnet build

# Testy
dotnet test
```

## Dokumentace

- [CLAUDE.md](CLAUDE.md) — system prompt pro Claude Code
- [docs/context.md](docs/context.md) — technický referenční kontext
- [docs/tasks.md](docs/tasks.md) — task backlog
- [docs/master-prompt.md](docs/master-prompt.md) — plný master prompt

## Licence

Proprietární. © 2026 Petr Türkott.
