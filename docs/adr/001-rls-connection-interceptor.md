# ADR 001 — RLS enforcement via DbConnectionInterceptor

- **Status:** Accepted
- **Date:** 2026-04-24
- **Tasks:** 1.2, 1.3

## Context

`docs/context.md` B.4 a `docs/tasks.md` TASK 1.3 specifikují multi-tenant
izolaci takto:

> V `DbContext.SaveChangesAsync` před každým dotazem:
> `SET LOCAL app.current_tenant_id = '<uuid>'`
> Použij `DbContext.Database.ExecuteSqlInterpolatedAsync` v custom
> `ISaveChangesInterceptor`.

RLS policy využívá `current_setting('app.current_tenant_id')` v `USING` i
`WITH CHECK` klauzuli pro `tenant_isolation` politiku nad `users` (a budoucími
tenant-scoped tabulkami).

## Problém se spec

1. `ISaveChangesInterceptor` se spouští pouze při `SaveChangesAsync`, tj.
   **jen pro zápisy**. Čtení (EF `.ToListAsync`, `.FirstOrDefaultAsync`,
   LINQ queries) touto cestou neprochází a RLS by tak u čtení nikdy
   nedostala platný `current_setting` → policy vyfiltruje všechna data
   (nebo selže při castu prázdného stringu na uuid).
2. `SET LOCAL` platí pouze v rámci explicitní transakce. EF default
   auto-commit režim mimo `BeginTransaction` znamená, že každý příkaz
   běží v implicitní transakci, která skončí okamžitě → `SET LOCAL` se
   ztratí před dalším příkazem ve stejném `SaveChanges` batch.

Spec tedy nekryje čtení a je rozbitá i pro zápisy mimo explicitní
transakci. Zachovat literu specifikace znamená mít RLS, která je
funkčně rozbitá.

## Rozhodnutí

Implementujeme izolaci přes **`DbConnectionInterceptor.ConnectionOpenedAsync`**
(viz [TenantContextInterceptor](../../src/AzKotle.Infrastructure/Persistence/Interceptors/TenantContextInterceptor.cs))
s `SELECT set_config('app.current_tenant_id', <uuid>, false)` — session-level
setting. Platí po celou dobu držení fyzického spojení (tj. pokrývá jak
čtení, tak zápis v libovolné kombinaci transakcí).

Při návratu spojení do poolu Npgsql volá `DISCARD ALL` (default), což
session-level `SET` resetuje → žádný leak mezi requesty/tenanty.

## Důsledky

- **Pozitivní:** RLS skutečně chrání čtení i zápis. Test `WITH CHECK`
  v [TenantIsolationTests](../../tests/AzKotle.Api.IntegrationTests/Persistence/TenantIsolationTests.cs)
  ověřuje, že cross-tenant INSERT selže. Testy běží pod non-superuser
  rolí (`azkotle_app`, `NOBYPASSRLS`), protože superuser RLS obchází.
- **Neutrální:** Místo per-SaveChanges setu máme per-connection-open set.
  V `HttpTenantContext` se `tenant_id` nastaví na začátku requestu a
  platí pro všechna DB volání v rámci requestu (i když EF fyzicky
  otevře víc connections).
- **Negativní:** Vyžadujeme `FORCE ROW LEVEL SECURITY` na tabulkách,
  aby RLS platila i pro vlastníka tabulky (table owner default RLS
  obchází). Produkční DB role musí být `NOBYPASSRLS` (zaznamenáno pro
  TASK 4.1/4.2).

## Alternativy zamítnuté

- **`ISaveChangesInterceptor` + `SET LOCAL` (dle spec)** — nepokrývá
  čtení, viz výše.
- **Kombinace obou** — duplikace logiky bez přidané hodnoty; connection
  interceptor sám o sobě stačí.
- **Globální EF query filter (`HasQueryFilter`)** — funguje, ale filtruje
  na úrovni .NET, ne DB. Cross-tenant leak je možný přes raw SQL,
  `FromSqlRaw`, nebo chyby v mapování. RLS v DB je defense-in-depth,
  nepřenecháváme to aplikaci.

## Reference

- PostgreSQL RLS: <https://www.postgresql.org/docs/16/ddl-rowsecurity.html>
- Npgsql connection pool `DISCARD ALL`: <https://www.npgsql.org/doc/connection-string-parameters.html>
- EF Core Interceptors: <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors>
