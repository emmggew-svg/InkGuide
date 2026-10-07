# InkGuide — AI_SETUP

> Optional AI file descriptions (Anthropic). Off by default. This document
> describes the setup and the privacy guards. The provider implementation is
> owned by the platform workstream; the host surface described here is shipped.

## Status

`ai.getStatus` returns one of `notConfigured | consentRequired | ready |
budgetExceeded | error`. Enabling requires **both** `ai.setKey` **and**
`ai.acknowledgeDataSharing=true` (consent is recorded with a timestamp).
Per-repo opt-out is supported. The key is stored DPAPI-protected, never in
`settings.json`, never logged (the host redacts `sk-ant-` and `x-api-key`).

## Setup

1. Get an API key from the [Anthropic console](https://console.anthropic.com/).
   Keys are billed per token — check current pricing on Anthropic's pricing
   page before enabling; typical file-description excerpts cost fractions of
   a cent each, but bulk re-analysis of a large workspace adds up.
2. In InkGuide → Settings → AI: paste the key (`ai.setKey`), tick the
   data-sharing acknowledgement (`ai.acknowledgeDataSharing`), optionally
   change the model (default is configurable; verify the model id against
   current Anthropic docs) and the daily request budget (default 500).
3. Run `ai.test` (harmless connectivity check). Status should read `ready`.

## Privacy guards (enforced by the provider, not by trust)

- AI is opt-in, default off; every enablement records consent + timestamp.
- Never sent: `Sensitive` nodes (secrets, keys, credentials), binaries,
  images (vision off by default).
- Excerpt cap (default 6 KB per file) with obvious-secret stripping.
- Daily request budget → `AI_BUDGET_EXCEEDED` instead of silent spending.
- File contents are untrusted data in the prompt; the model has no tools and
  its output is validated JSON written only to description/insight fields —
  never executed, never used to change files or paths. It never replaces
  user-authored text (precedence user > ai > rule).
- `ai.clearKey` deletes the key; `accounts.disconnect` + data wipe removes
  consent records with everything else.

## Costs (honest)

Anthropic bills input + output tokens. The app sends one bounded request per
analyzed file (metadata + ≤ 6 KB excerpt + sibling/project context) and
receives a short strict-JSON reply. Re-analyzing 10,000 files will cost real
money — set the daily budget, use per-repo opt-out for boring folders, and
watch `ai.getUsage`. No AI traffic happens unless status is `ready`.
