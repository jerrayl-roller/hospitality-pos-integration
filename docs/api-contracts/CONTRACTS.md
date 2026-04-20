# API Contracts Index

This document indexes all API contracts for the ROLLER F&B POS prototype. Each contract must be reviewed and signed off before the phases that depend on it can begin.

---

## Status Summary

| Contract | Type | Status | Blocks |
|----------|------|--------|--------|
| [Existing Endpoints](existing-endpoints.md) | Existing ROLLER API | REVIEWED — 3 outstanding TODOs remain (see file) | Phase 1b (catalogue), Phase 2 (booking import) |
| [Payments — Giftcard update](payments.md) | Existing endpoint — change | DRAFT — pending ROLLER team implementation | Phase 5 (gift card settlement) |
| [Webhook](webhook.md) | Inbound to POS | INCOMPLETE — manual review required | Phase 4 (webhook handling) |
| [Payment Lock / Unlock](payment-lock.md) | New ROLLER endpoint | DRAFT — pending ROLLER team approval | Phase 3 (lock integration) |
| [Push F&B Charges](push-charges.md) | New ROLLER endpoint | DRAFT — pending ROLLER team approval | Phase 6 (ROLLER sync) |
| [Gift Card Operations](gift-card.md) | New ROLLER endpoint | DRAFT — pending ROLLER team approval | Phase 5 (gift card payment) |

---

## Completion Checklist (Phase 0 Gate)

The following must all be true before Phase 1b begins:

- [ ] `existing-endpoints.md` — all five endpoints reviewed, TODO fields filled in
- [ ] F&B `productType` / `productSubType` enum values confirmed against a **live API response** (not just docs)
- [ ] `webhook.md` — signature mechanism confirmed; payload shape confirmed
- [ ] `payment-lock.md` — approved by ROLLER backend team
- [ ] `push-charges.md` — approved by ROLLER backend team; overlap with "Add Transaction Record" resolved
- [ ] `gift-card.md` — confirmed whether ROLLER already has gift card endpoints; approved by ROLLER backend team

---

## How to Complete the Existing Endpoints Review

1. Open each docs URL from `existing-endpoints.md` in a **real browser** (the pages are JS-rendered SPAs and cannot be fetched programmatically).
2. Fill in all `TODO` fields in the table for that endpoint.
3. For the Products endpoint specifically, also make a live API call to confirm the actual `productType` / `productSubType` values in real data:

```bash
curl -s \
  -H "Authorization: Bearer $ROLLER_API_KEY" \
  "https://api.roller.app/TODO-CONFIRM-PATH" \
  | jq '[.[] | {productType, productSubType}] | unique'
```

4. Mark the endpoint's **Status** field from `TODO` to `Reviewed — {date}`.
5. Update the status column in this index.

---

## Conventions

- All amounts are **integer cents** (e.g. `$10.00` = `1000`). Never use floats for money.
- All timestamps are **ISO 8601 UTC** (e.g. `2026-04-20T03:00:00Z`).
- All new endpoints use `idempotencyKey` in the request body (not a header) for retry safety.
- Authentication is OAuth 2.0 client credentials. `POST /token` with `client_id` + `client_secret` → cache the returned bearer token → `Authorization: Bearer {access_token}` on all calls. Token TTL is currently 86400s but read from response. See `existing-endpoints.md` Authentication section.
