# Postgres deploy poznámky

## Proč `azkotle_app` (non-superuser) pro runtime API

Postgres docker image vytvoří `POSTGRES_USER` jako **SUPERUSER**. Superuseři
**vždy obcházejí RLS politiky**, i když má tabulka `FORCE ROW LEVEL SECURITY`.
To znamená, že pokud by API běželo s `POSTGRES_USER` přímo, celá `tenant_isolation`
RLS architektura by byla v produkci bezcenná — jeden tenant by viděl data všech
ostatních.

Řešení: oddělené role.

| Role | Privilege | Použití |
|------|-----------|---------|
| `${POSTGRES_USER}` (default `azkotle`) | SUPERUSER | Migrace (DDL), `pg_dump`, bootstrap aplikační role |
| `azkotle_app` | LOGIN, NOSUPERUSER, NOBYPASSRLS | Runtime API connection (RLS skutečně enforcuje) |

## Init scripty (`init/`)

Postgres docker image spustí všechny `*.sh` a `*.sql` soubory v
`/docker-entrypoint-initdb.d/` v abecedním pořadí — **ale jen při PRVNÍM
vytvoření datového adresáře**. Na již existujícím volume se NESPUSTÍ.

`init/01-create-app-role.sh`:
- Vytvoří roli `azkotle_app` (NOSUPERUSER NOBYPASSRLS) s heslem z
  `APP_DB_PASSWORD` env var.
- Udělí potřebná práva na schema `public`.
- Nastaví `ALTER DEFAULT PRIVILEGES` aby budoucí migrace nezapomněly
  na grant.
- Po existenci `audit_log` tabulky odebere UPDATE/DELETE (append-only).
- **Idempotentní** — bezpečné spouštět opakovaně, jen aktualizuje heslo.

## Manuální runner pro existující volume (`apply-app-role.sh`)

Produkční DB je už nasazená a má vyplněné volume. Init script tedy automaticky
neproběhne. Postup:

```bash
# 1. Vygeneruj a vlož APP_DB_PASSWORD do .env.prod
APP_DB_PWD="$(openssl rand -base64 32)"
echo "APP_DB_PASSWORD=${APP_DB_PWD}" >> /opt/azkotle/deploy/.env.prod

# 2. Spusť runner (přečte .env.prod, vykoná init script přes docker exec)
cd /opt/azkotle/deploy
./postgres/apply-app-role.sh

# 3. Restart api kontejneru, aby použil novou roli
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate api

# 4. Smoke test
curl -fsS https://api.az-kotle.cz/health/ready
docker compose logs api --tail=100 | grep -iE 'error|warn|fatal'
```

### Rollback

Pokud `/health/ready` po restartu vrátí 503 (typicky chybí GRANT na nějakou
tabulku/sequence), proveď okamžitý rollback:

1. V `.env.prod` přepni `APP_DB_USER` zpět na `${POSTGRES_USER}` (nebo doplň
   `ConnectionStrings__AzKotleDb` přímo s původním usernamem).
2. `docker compose up -d --force-recreate api`
3. Zjisti chybějící GRANT v logu, doplň do `apply-app-role.sh`, opakuj.

## Migrace (DDL)

Migrace **se nespouští z runtime API kontejneru**. EF Core `Database.Migrate()`
v `Program.cs` neexistuje — záměrně. Migrace musí běžet jako admin role, který
umí `CREATE TABLE`, `ALTER`, atd.

Doporučený postup po novém migration commitu:

```bash
# Build image z aktuálního commitu (nebo čekej na CI build).
# Připoj se k běžícímu api kontejneru a spusť migrace ručně:

docker compose -f docker-compose.prod.yml --env-file .env.prod exec \
    -e ConnectionStrings__AzKotleDb="Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}" \
    api dotnet ef database update --project /app/AzKotle.Infrastructure.dll
```

(Detailní migration deploy postup TODO — viz issue tracker, mimo scope tohoto sprintu.)

Jakmile migrace přidá novou tabulku se sloupcem `tenant_id`, **musí** mít:

```sql
ALTER TABLE public.<table> ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.<table> FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON public.<table>
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
```

CI test `RlsCoverageTests.Every_table_with_tenant_id_has_force_rls_and_tenant_isolation_policy`
to vynucuje — bez toho zelený CI nepřejde.
