# AZ KOTLE — Technický referenční kontext

## B.1 Produkt

**AZ KOTLE** je B2B SaaS pro revizní techniky plynových spotřebičů v ČR. Řeší:

- Evidenci kotlů u zákazníků s QR kódy (fyzický štítek → scan → historie).
- Generování revizních zpráv dle **NV 191/2022 Sb.** (roční prohlídka spalinových cest) a **TPG 704 01** (servis plynového zařízení) do PDF.
- Sledování termínů příštích revizí s automatickými připomínkami (email + push).
- CRM pro zákazníky a lokality.
- Fakturaci (v. 2, mimo MVP — napojení na Fakturoid/iDoklad API).

**Cílová persona:** OSVČ revizní technik nebo servisní firma 2–10 techniků.
**Pricing:** 490 / 690 / 990 Kč/user/měsíc (Solo / Pro / Business).
**MVP launch:** Q3 2026, first paying customer: Q4 2026.

## B.2 Tech Stack

| Vrstva         | Technologie                                                     |
| -------------- | --------------------------------------------------------------- |
| Frontend       | **Blazor United (.NET 10)** — SSR + interaktivní WASM islands   |
| Mobile         | **.NET MAUI Blazor Hybrid** (sdílené Blazor komponenty) — fáze 2 |
| Backend API    | **ASP.NET Core 10 Minimal API** (`/api/v1/*`)                   |
| ORM            | **EF Core 10** + Npgsql                                         |
| DB             | **PostgreSQL 16** (multi-tenant RLS)                            |
| Cache          | **Redis 7** (session store, rate limiting)                      |
| Storage        | **Backblaze B2** (S3-compatible) přes AWS SDK                   |
| Auth           | **ASP.NET Core Identity** + JWT (refresh token rotation)        |
| Reverse proxy  | **Caddy 2** (auto-HTTPS, Let's Encrypt)                         |
| Container      | **Docker + docker-compose** (prod), později k8s                 |
| CI/CD          | **GitHub Actions** → **GHCR** → VPS (SSH deploy)                |
| Monitoring     | **Seq** (logs) + **Grafana + Prometheus** (metrics)             |
| Error tracking | **Sentry** (.NET + Blazor)                                      |
| PDF            | **QuestPDF** (komerční licence pro produkci)                    |
| QR             | **QRCoder** (open source)                                       |
| Validace       | **FluentValidation 11**                                         |
| Testy          | **xUnit + FluentAssertions + NSubstitute + Testcontainers**     |

**VPS:** Forpsi Basic Ubuntu 24, IP `80.211.223.147`, doména `az-kotle.cz`.

**Subdomény:**
- `az-kotle.cz` — marketingový web (statický, fáze 0)
- `app.az-kotle.cz` — Blazor United aplikace
- `api.az-kotle.cz` — REST API (pro mobilní app, integrace)
- `admin.az-kotle.cz` — super-admin panel (interní)

## B.3 Solution struktura

```
az-kotle/
├── src/
│   ├── AzKotle.Domain/              # Entities, value objects, domain events
│   ├── AzKotle.Application/         # Use cases, services, DTOs, validátory
│   ├── AzKotle.Infrastructure/      # EF Core, Redis, B2, external APIs
│   ├── AzKotle.Web/                 # Blazor United (SSR + WASM)
│   ├── AzKotle.Api/                 # Minimal API host
│   └── AzKotle.Shared/              # Sdílené DTO, constants
├── tests/
│   ├── AzKotle.Domain.Tests/
│   ├── AzKotle.Application.Tests/
│   ├── AzKotle.Api.IntegrationTests/
│   └── AzKotle.Web.E2ETests/        # Playwright
├── docs/
│   ├── context.md
│   ├── adr/                         # Architecture Decision Records
│   └── api.md                       # OpenAPI odkazy
├── deploy/
│   ├── docker-compose.prod.yml
│   ├── Caddyfile
│   └── .env.example
├── .github/workflows/
│   ├── ci.yml
│   └── deploy.yml
├── CLAUDE.md
├── README.md
└── AzKotle.sln
```

**Závislosti směrem dovnitř:** Web/Api → Application → Domain. Infrastructure → Application + Domain.
Domain nemá **žádné** externí závislosti (čistý C#).

## B.4 Datový model (ERD)

PostgreSQL 16. Všechny tabulky (kromě `identity.*`) mají sloupec `tenant_id UUID NOT NULL` a jsou pod RLS policy.

### Core tabulky

```
tenants
  id UUID PK
  slug VARCHAR(64) UNIQUE
  company_name VARCHAR(255)
  ico VARCHAR(8)
  dic VARCHAR(16)
  plan VARCHAR(32)                   -- 'solo' | 'pro' | 'business'
  seats_limit INT
  trial_ends_at TIMESTAMPTZ
  status VARCHAR(16)                 -- 'trial' | 'active' | 'suspended' | 'churned'
  created_at TIMESTAMPTZ DEFAULT NOW()
  updated_at TIMESTAMPTZ

users
  id UUID PK
  tenant_id UUID FK → tenants.id
  email VARCHAR(255) UNIQUE
  full_name VARCHAR(255)
  role VARCHAR(32)                   -- 'owner' | 'admin' | 'technician'
  technician_license_no VARCHAR(64)
  phone VARCHAR(32)
  is_active BOOLEAN DEFAULT TRUE
  last_login_at TIMESTAMPTZ

customers
  id UUID PK
  tenant_id UUID FK
  type VARCHAR(16)                   -- 'person' | 'company'
  name VARCHAR(255)
  ico VARCHAR(8) NULLABLE
  email VARCHAR(255) NULLABLE
  phone VARCHAR(32) NULLABLE
  notes TEXT
  created_at TIMESTAMPTZ

locations
  id UUID PK
  tenant_id UUID FK
  customer_id UUID FK → customers.id
  street VARCHAR(255)
  city VARCHAR(128)
  zip VARCHAR(16)
  gps_lat DECIMAL(10,7) NULLABLE
  gps_lon DECIMAL(10,7) NULLABLE
  notes TEXT

boilers
  id UUID PK
  tenant_id UUID FK
  location_id UUID FK → locations.id
  qr_code VARCHAR(16) UNIQUE
  manufacturer VARCHAR(128)
  model VARCHAR(128)
  serial_no VARCHAR(64)
  output_kw DECIMAL(5,1)
  fuel_type VARCHAR(32)              -- 'natural_gas' | 'lpg'
  installed_at DATE
  last_inspection_at DATE NULLABLE
  next_inspection_due DATE NULLABLE
  notes TEXT

inspections
  id UUID PK
  tenant_id UUID FK
  boiler_id UUID FK → boilers.id
  technician_id UUID FK → users.id
  inspection_type VARCHAR(32)        -- 'annual_nv191' | 'tpg704_01_service' | 'emergency'
  performed_at TIMESTAMPTZ
  status VARCHAR(16)                 -- 'draft' | 'signed' | 'archived'
  form_data JSONB
  findings TEXT
  recommendations TEXT
  next_due_at DATE
  pdf_b2_key VARCHAR(512) NULLABLE
  pdf_sha256 VARCHAR(64) NULLABLE
  signed_at TIMESTAMPTZ NULLABLE
  signature_data BYTEA NULLABLE
  created_at TIMESTAMPTZ DEFAULT NOW()

inspection_photos
  id UUID PK
  tenant_id UUID FK
  inspection_id UUID FK
  b2_key VARCHAR(512)
  caption VARCHAR(255)
  uploaded_at TIMESTAMPTZ

audit_log
  id BIGSERIAL PK
  tenant_id UUID
  user_id UUID
  action VARCHAR(64)
  entity_type VARCHAR(64)
  entity_id UUID
  metadata JSONB
  ip_address INET
  user_agent TEXT
  created_at TIMESTAMPTZ DEFAULT NOW()
```

### RLS policy

```sql
ALTER TABLE boilers ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON boilers
  USING (tenant_id = current_setting('app.current_tenant_id')::UUID);
```

V `DbContext.SaveChangesAsync` před každým dotazem:
```sql
SET LOCAL app.current_tenant_id = '<uuid>';
```
Použij `DbContext.Database.ExecuteSqlInterpolatedAsync` v custom `ISaveChangesInterceptor`.

## B.5 Autentizace a autorizace

- **Login:** email + heslo → JWT (15 min) + refresh token (30 dní, rotace).
- **Password:** Argon2id, `Microsoft.AspNetCore.Identity` s custom PasswordHasher.
- **MFA:** TOTP (fáze 2).
- **Session:** Redis, klíč `session:{userId}:{jti}`.
- **Role:** `owner`, `admin`, `technician`. V MVP `admin` a `technician`.
- **Policy:** `RequireTenantMember`, `RequireTechnician`, `RequireOwner`.
- **API auth:** `Authorization: Bearer <jwt>` header, middleware nastaví `current_tenant_id` na DB connection.

## B.6 Bezpečnost

- TLS 1.3 only, HSTS (max-age=63072000; includeSubDomains; preload).
- CSP strict, žádné inline skripty (kromě Blazor bootstrap).
- Rate limiting: 100 req/min/IP anonymně, 1000 req/min/user.
- Secrets: nikdy v repu. `.env` + `docker secrets` / Key Vault (fáze 2).
- SQL injection: jen parametrizované dotazy.
- XSS: Blazor default-safe, nikdy `MarkupString` z user inputu.
- CSRF: antiforgery tokeny na všech POST/PUT/DELETE.
- Audit: každá destruktivní akce + každý `inspection.sign` → `audit_log`.

## B.7 Externí integrace (fáze 2+)

- **ARES** — autofill IČO → název, adresa.
- **Fakturoid API** — fakturace.
- **Pošta ČR** — validace PSČ.
- **SendGrid / Mailgun** — transakční emaily.
- **Firebase Cloud Messaging** — push notifikace (mobilní app).
