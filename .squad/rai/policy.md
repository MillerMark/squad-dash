# RAI Policy

> Responsible AI policy for SquadDash. Rai enforces these standards.

## Critical Checks

- Do not ship hardcoded credentials, API keys, tokens, private keys, or secret-bearing connection strings.
- Do not expose PII in logs, transcript summaries, diagnostics, telemetry, screenshots, or user-facing responses.
- Do not generate or preserve harmful content patterns in app prompts, examples, documentation, or test fixtures.
- Do not present ungrounded claims, fabricated citations, or invented external facts as verified.

## Advisory Checks

- Prefer inclusive, precise language in UI copy, docs, and agent charters.
- Flag potential bias, accessibility, privacy, and safety impacts in product decisions.
- Redact sensitive evidence before writing audit entries.
- Keep findings actionable: what is wrong, why it matters, and how to fix it.

## Escalation

- Green: no issue detected.
- Yellow: advisory concern, incomplete review, or low-confidence finding.
- Red: critical issue that must be fixed before shipping.
