# AZ KOTLE SaaS — Master Prompt pro Claude Code

**Verze:** 1.0
**Datum:** duben 2026
**Autor:** Petr Türkott (turkott@gmail.com)
**Cílový nástroj:** [Claude Code](https://docs.anthropic.com/claude-code)

---

## Jak tento dokument použít

1. Otevři Claude Code v prázdném adresáři (`mkdir az-kotle && cd az-kotle && claude`).
2. Zkopíruj **ČÁST A (System Prompt)** a vlož jako první zprávu. Claude si uloží kontext do `CLAUDE.md`.
3. Postupně zadávej jednotlivé úkoly z **ČÁSTI C (Task Backlog)** — jeden task = jedna Claude Code session. Nikdy nezadávej víc tasků najednou.
4. Po každém tasku: review kód, spusť testy, commit, až poté další task.
5. **ČÁST B (Technický referenční kontext)** drž v `docs/context.md` v repozitáři — Claude si ji bude číst při každém větším rozhodnutí.

---

# ČÁST A — SYSTEM PROMPT (v CLAUDE.md)

Viz [../CLAUDE.md](../CLAUDE.md).

# ČÁST B — TECHNICKÝ REFERENČNÍ KONTEXT

Viz [context.md](context.md).

# ČÁST C — TASK BACKLOG

Viz [tasks.md](tasks.md).

---

# ČÁST D — Jak pracovat s Claude Code denně

## Doporučený workflow pro každý task

1. **Příprava (mimo Claude):**
   - Přečti si cíl tasku + acceptance criteria.
   - Ověř, že předchozí task je fully done (commitnutý, testy prochází).

2. **Kick-off v Claude Code:**
   ```
   Pracujeme na TASK X.Y z docs/tasks.md. Přečti si popis a napiš
   krátký implementační plán (3-7 bulletů). Nepiš kód, jen plán.
   ```

3. **Review plánu:** schval / oprav / doplň. Až pak:
   ```
   Plán OK, pokračuj s implementací. Commituj atomicky.
   ```

4. **Průběžně:**
   - Když Claude přidá závislost (NuGet), zeptej se PROČ ji přidal.
   - Když se zacyklí nebo opakuje chyby, udělej `/clear` a začni tento task znovu s upřesněnou zprávou.

5. **Před uzavřením tasku:**
   ```
   Projdi acceptance criteria z tasku X.Y a řekni, co je DONE a co chybí.
   ```
   - Spusť `dotnet test`, `dotnet format --verify-no-changes`.
   - Code review commitů (alespoň hlavní).
   - Merge do main jen když je 100 % done.

## Anti-patterny

- **Nedávej Claude víc tasků najednou.**
- **Nezadávej "udělej celý projekt".**
- **Nenech Claude vymýšlet architekturu.** Je v B. Jen se jí drží.
- **Nepřeskakuj review.**
- **Nekombinuj Claude Code s manuálními úpravami ve stejném tasku** bez explicitního sdělení.

---

# ČÁST E — Prompty-vzorky

### Když něco nefunguje

```
Test X selhává s chybou Y. Nepiš opravu hned. Nejdřív:
1. Zjisti root cause.
2. Popiš mi, co se děje a proč.
3. Navrhni 2 možné opravy s trade-offs.
Až pak počkej na schválení a oprav.
```

### Code review existujícího souboru

```
Udělej code review souboru X.cs. Zaměř se na bezpečnost (RLS, injection),
async correctness, testovatelnost, čistotu. Najdi top 5 issues podle závažnosti.
```

### Nový feature mimo backlog

```
Klient chce feature Z. Než kódujeme:
1. Přečti docs/context.md a najdi architektonické místo.
2. Napiš ADR v docs/adr/NNN-feature-z.md.
3. Rozbij na 3-5 tasků dle formátu ČÁST C.
Počkej na schválení.
```

---

# ČÁST F — Kontext specifický pro AZ KOTLE

## Legislativní požadavky (MUST HAVE v MVP)

### NV 191/2022 Sb. — Roční kontrola spalinových cest

Povinné položky:
- Identifikace provozovatele
- Identifikace spalinové cesty (typ, materiál, průměr, délka)
- Datum, technik, oprávnění TIČR
- Měření tahu komína
- Stav (čistá, znečištěná, defekty)
- Závady + doporučení
- Termín příští kontroly (do 12 měsíců)
- Podpisy

### TPG 704 01 — Servis plynového zařízení

Povinné položky:
- Identifikace spotřebiče (typ, výrobce, výkon)
- Kontrola těsnosti
- Měření spalin (CO, CO2, teplota, účinnost)
- Stav hořáku, výměníku, regulace
- Nastavení přetlaku plynu
- Závady + opatření
- Podpisy

**Důležité:** oba dokumenty musí být **právně platné** — PDF/A + (v. 2) ElevatedID digitální podpis. V MVP stačí sken podpisu zákazníka.

## Brand (design tokens)

- Primary: `#0F6B8A` (deep teal)
- Accent: `#D97706` (warm amber)
- Text: `#0F1A24`
- Muted: `#5A6776`
- Border: `#D3DAE0`
- Surface: `#F4F6F8`
- Font: Inter (UI), DM Sans (headlines)

## Doména a kontakt

- **Doména:** az-kotle.cz (Forpsi)
- **VPS:** 80.211.223.147 (Forpsi Basic, Ubuntu 24.04)
- **User na VPS:** `petr` (sudo, SSH key ed25519)
- **Kontakt:** turkott@gmail.com

## Odchylka od dokumentu: .NET verze

Dokument říká .NET 8. V projektu používáme **.NET 10 (LTS)** — .NET 8 není na stroji, .NET 10 je aktuální LTS s podporou do 2028. Všechny odkazy na .NET 8 v tomto dokumentu čti jako .NET 10.
