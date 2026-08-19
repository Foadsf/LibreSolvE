# LibreSolvE / EES Compatibility Contract

**Status:** Phase 0 of `PLAN.md`. This document is normative for everything
that follows — code is measured against it, not the other way around, and a
change to accepted syntax means editing this file first.

**Evidence base:** the EES manual (F-Chart Software, copyright 1992-2000,
`03_EES/EES-manual.pdf`), plus two independently-sourced modern materials that
predate-check it: an MIT 2020 course deck and a 2016 heat-transfer course
(`mep460_using_professional_ees.pdf`, both in `03_EES/`). Every rule below cites
which source it came from, because the manual is 25 years old and several
constructs it doesn't mention turn out to be real, later EES syntax.

---

## 1. The contract

> **A.** Every `.lse` file LibreSolvE accepts must parse and run, unmodified, in
> EES, producing the same values.
>
> **B.** Every EES file within the supported subset (§6) must parse and run in
> `lse`, producing the same values.

**A is the binding constraint.** The LibreSolvE grammar can never accept more
than EES accepts. Anything LibreSolvE needs that EES does not is carried
inside a comment (`{ ... }` or `" ... "`), which EES already parses and
discards — that is the whole mechanism, and it needs no new syntax to exist.

---

## 2. Rule: comments — exactly two forms

> "Comments must be enclosed within braces `{ }` or within quote marks `" "`.
> Comments may span as many lines as needed."
> — manual, general rules, item 3

`//` is not one of the two forms. It is a LibreSolvE invention with no EES
meaning, and it is the first of the current violations (§5).

---

## 3. Rule: assignment operators are scope-dependent

- `=` at main scope is an **equation** — order-independent, solved as part of
  the simultaneous system.
- `:=` at main scope is a **syntax error**. `:=` is legal only inside
  `FUNCTION` / `PROCEDURE` bodies.
  — manual, `FUNCTION`/`PROCEDURE` worked examples (`FUNCTION psi(...)`,
  `PROCEDURE test(...)`)

The branch already enforces this (`AstBuilderVisitor.VisitExplicitAssignment`
throws `ParsingException` at main scope). Second violation, already partly
fixed on the branch; the corpus (examples 003–024) is not yet migrated to it.

---

## 4. Rule: units are inline and unquoted, not a string comment

Confirmed independently on two modern sources, both showing real EES
equations verbatim:

```
T1=50 [C]      P1=300 [kPa]
m_dot_h=0.1 [kg/s]    U=60 [W/m^2-K]    k=200 [W/m-K]
```

This is a **different mechanism** from what the original 2025 LibreSolvE
design used — `L := 1.0 "[m]"`, a quoted-string comment. That form is also
legal EES (quote marks are one of the two comment forms, §2) and happens to
parse, but it is not how EES itself carries units, and a value's actual
units in EES are not read from an arbitrary trailing comment string.

**Grammar note for Phase 4, cited exactly rather than recalled:**
`EesLexer.g4` currently defines
`ID : [a-zA-Z] [a-zA-Z0-9_]* ('$' | LBRACK)? ;` — the trailing `[` is
consumed **into the identifier token itself**, so `X[5]` does not tokenize as
`ID LBRACK NUMBER RBRACK`; it tokenizes as one `ID` token spanning `X[`. Real
`[unit]` support must disambiguate a numeric-literal's trailing `[C]` from an
identifier's trailing `[5]` array index, and cannot simply extend the current
`ID` rule without breaking one or the other.

---

## 5. Rule: `PLOT` is not an EES statement

Zero hits for a `PLOT` keyword or statement anywhere in the manual. EES
plotting is driven from the GUI (New Plot Window dialog), not from equation
text. The `PLOT_CMD` token in the current grammar is a LibreSolvE invention
with no EES equivalent. Per the compatibility contract it must either move
inside a comment directive (`{$PLOT t, T1, T2}`) or become a CLI-only flag
with no textual representation in the `.lse` file at all. Third violation.

---

## 6. Directive whitelist

**Confirmed real, manual (1992-2000):**

| Directive | Manual reference |
|---|---|
| `$INCLUDE` | loads a library/text file of equations |
| `$EXPORT` | writes selected variables to CSV/lookup-format ASCII |
| `$IMPORT` | reads variables written by `$EXPORT` |
| `$COMMON` | passes values from main program into a function/procedure, one-way |
| `$COMPLEX` | enables complex-number arithmetic |
| `$WARNINGS` | (listed; behaviour not yet transcribed in detail) |
| `$OPENLOOKUP` | Professional version only — do not assume it's available |
| `$SAVELOOKUP` | writes the active Lookup table to disk |
| `$ATTRIBUTES` | Professional version only, per the manual |
| `$IntegralTable` | routes `Integral()` intermediate values into a table — confirmed at three separate points in the manual |

**Confirmed real, but only via the modern sources — absent from the 2000
manual because it predates them:**

| Directive | Confirmed via |
|---|---|
| `$UnitSystem SI\|Eng <mass> <temp> <pressure> <energy>` e.g. `$UnitSystem SI K Pa J` | mep460.md, real course code, multiple occurrences |

(`UnitSystem('SI')` — the parenthesised **function** form, used inside
`FUNCTION`/`PROCEDURE` bodies to query the active setting — is separately
confirmed in the manual itself, manual §Chapter 4. It is a different
construct from the `$UnitSystem` directive and both are legitimate.)

**UNCONFIRMED — appears in the LibreSolvE example corpus, found in NEITHER
the manual NOR either modern source:**

| Construct | Where it appears | Status |
|---|---|---|
| `$IntegralAutoStep Vary=1 Min=<n> Max=5000 Reduce=1e-4 Increase=1e-6` | `examples/013_PendulumWithUnits.lse` (`Min=20`), `examples/019_HeatTransfer1D.lse` (`Min=50`) — same directive and keyword set, different parameter values | **Not verified EES syntax.** Do not ship as an accepted main-grammar construct until confirmed against a live EES install or a newer manual. If it survives the migration in some form, it rides inside a comment directive like every other LibreSolvE-only construct — it does not get main-grammar acceptance on the strength of this corpus alone. |

This is a fourth item beyond the three `PLAN.md` originally listed, found by
checking the directive against the manual rather than assuming the corpus
was already EES-legal.

---

## 7. Phase 3 (MVP) grammar scope

In scope for the MVP line (`PLAN.md` §5, end of Phase 3):

- Equations (`=`) and `FUNCTION`/`PROCEDURE`-scoped assignment (`:=`)
- Arithmetic expressions, function calls, the built-in math functions already
  wired (`SIN`, `COS`, `LOG`, `EXP`, `SQRT`, `^`/`**`)
- Comments: `{ ... }` and `" ... "` only
- `Integral()` for ODEs, `$IntegralTable`
- The confirmed directive whitelist (§6, first two tables)

Out of scope for MVP, deferred to later phases per `PLAN.md` §5-6:

- Units (`[unit]` inline syntax) — Phase 4
- Arrays, `DUPLICATE`, `SUM` — Phase 5
- Thermophysical properties (CoolProp) — Phase 6
- `$IntegralAutoStep` — blocked on the open question above; not a phase yet

---

## 8. The open EES-availability dependency

Restated from `PLAN.md` §2, because it still isn't answered: direction A is
only **verifiable** by running the file in real EES. Two worked examples with
printed solutions exist in the corpus already —

```
{Solution: eta=0.8063}   (mep460.md, straight-fin efficiency)
{Solution: eta=0.7008}   (mep460.md, spine-fin efficiency)
```

— and serve as a partial numeric oracle even without a live install. They do
NOT verify parse-acceptance in EES itself, which is the harder and more
important half of direction A. Phase 2's conformance harness should treat
"parses in EES" and "computes the right number" as two separate gates, only
one of which the current corpus can check.

---

## 9. License

**LGPL-3.0**, as currently shipped (`LICENSE`). The original 2025-05-09
design conversation specified MIT; this document deliberately keeps
LGPL-3.0 instead of silently reverting to that original choice.

Reasoning, recorded 2026-08-19: no external contributors exist yet, so
changing this later costs nothing — there is no one else's copyright to
renegotiate. LGPL's copyleft is the mechanism that stops someone taking this
project and shipping a closed, proprietary EES-compatible fork, which lines
up with the project's own founding principle ("FLOSS First") better than a
permissive license would. Revisit if that calculus changes — e.g. if
adoption as a library inside proprietary tooling becomes a goal.
