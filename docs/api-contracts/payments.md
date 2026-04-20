# Add Transaction Record — Updated Contract

**Status:** DRAFT — pending ROLLER team implementation  
**Phase:** Built by ROLLER team before Phase 5 (gift card settlement)  
**Change type:** Extension to an existing endpoint — additive, backwards compatible

---

## Overview

The existing `POST /bookings/{uniqueId}/payments` endpoint records a payment against a ROLLER booking. It currently does not support `Giftcard` as a `paymentType`. This document describes the required change.

**The only modification required is adding `Giftcard` to the `paymentType` enum.** No other fields change. Existing callers using other `paymentType` values are unaffected.

---

## Endpoint

```
POST https://api.roller.app/bookings/{uniqueId}/payments
Authorization: Bearer {access_token}
Content-Type: application/json
```

### Request body — current (unchanged fields)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Caller-provided unique identifier. Acts as idempotency key. Recommended format for gift card payments: `{tabId}-giftcard-payment`. |
| `paymentType` | string | Yes | See enum below. |
| `amount` | number | Yes | Decimal float. Negative for refunds. Venue local currency. |
| `creditCardFees` | number | No | Fees to add (not applicable for gift card payments). |
| `transactionDate` | string | No | ISO 8601 UTC. Defaults to now if omitted. |

### `paymentType` enum — updated

| Value | Status | Notes |
|-------|--------|-------|
| `CreditCard` | Existing | |
| `CreditCardPreAuth` | Existing | |
| `Cash` | Existing | |
| `Cheque` | Existing | |
| `BankTransfer` | Existing | |
| `Other` | Existing | |
| `Giftcard` | **New — to be added** | Used after a successful gift card deduction via `POST /gift-cards/{cardNumber}/deduct`. The `id` field should be set to the `transactionId` returned by the deduction endpoint for traceability. |

### Response (unchanged)

```json
{ "uniqueId": "ffdae420-a912-4d93-9234-9faca5e6194f" }
```

Returns the booking's `uniqueId`. No payment record ID is returned — the POS uses the caller-provided `id` field as its own reference.

---

## Gift Card Payment Example

After a successful gift card deduction, the POS posts:

```json
{
  "id": "gc_txn_7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "paymentType": "Giftcard",
  "amount": 41.80,
  "transactionDate": "2026-04-20T04:15:00Z"
}
```

Where `id` is the `transactionId` returned by `POST /gift-cards/{cardNumber}/deduct`. This links the booking payment record back to the gift card transaction.

---

## Context — Full Gift Card Settlement Flow

This endpoint is step 3 of 5 in the gift card settlement sequence. See `gift-card.md` for the complete flow. The rollback rule if this call fails: the POS must call `POST /gift-cards/{cardNumber}/refund` to reverse the deduction before surfacing the error to the operator.

---

## Open Questions for ROLLER Team

1. **Validation on `Giftcard` payments:** Should the endpoint validate that the gift card `transactionId` supplied as `id` actually exists and matches the amount? Or is it purely a recording endpoint with no cross-validation?
2. **Refund path:** For a gift card payment recorded here, how should a refund be posted — negative `amount` with `paymentType: "Giftcard"`, or via the gift card refund endpoint only? Both are likely needed for full reconciliation.

---

## Sign-off

| Reviewer | Role | Date | Approved |
|----------|------|------|----------|
| | ROLLER Backend | | |
| | POS Lead | | |
