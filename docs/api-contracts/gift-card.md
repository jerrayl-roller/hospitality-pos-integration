# Gift Card Operations — New ROLLER Endpoint Contracts

**Status:** DRAFT — pending ROLLER team review and approval  
**Phase:** Designed in Phase 0 (T0.6) · Integrated by POS in Phase 5 (T5.3)  
**Note:** ROLLER may have existing gift card endpoints. Confirm against live docs before building these from scratch. If partial support exists, extend rather than duplicate.

---

## Overview

The full gift card settlement flow requires **three new endpoints** (designed here) plus **one change to an existing endpoint**:

1. **Balance Lookup** *(new)* — check the available balance before attempting payment
2. **Deduction** *(new)* — redeem value from the card
3. **Refund** *(new)* — reverse a deduction (for failed settlement recovery)
4. **Post payment record** *(existing endpoint updated)* — after a successful deduction, call `POST /bookings/{uniqueId}/payments` with `paymentType: "Giftcard"` to record the payment against the booking. ROLLER team must add `Giftcard` to that endpoint's `paymentType` enum. See `existing-endpoints.md` §5.

**Key design constraints:**
- Balance lookup and deduction are called sequentially at settlement time. A race condition (balance changes between lookup and deduction) is handled by the deduction endpoint returning `422 insufficient_balance`.
- **Amount format:** Use **decimal float** (e.g. `41.80`) to match ROLLER's existing payment conventions. The POS stores amounts internally in cents and converts on the way out (÷ 100). The ROLLER team designing this endpoint should follow the same decimal convention as the existing `/payments` endpoint.
- Deduction and Refund both use caller-generated `idempotencyKey` — safe to retry.
- For the prototype, split payments (gift card + card) are out of scope. If the gift card balance is less than the tab total, return an error.

---

## Endpoint 1 — Balance Lookup

### Request

```
GET /api/v1/gift-cards/{cardNumber}/balance
Authorization: Bearer {apiKey}
```

**Path parameters**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `cardNumber` | string | Yes | The ROLLER gift card number as entered by the operator |

### Responses

**200 OK**

```json
{
  "cardNumber": "1234567890123456",
  "balance": 50.00,
  "currency": "AUD",
  "status": "active"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `cardNumber` | string | Echo of the path parameter. |
| `balance` | number | Remaining balance as decimal float (e.g. `50.00`). |
| `currency` | string | ISO 4217. |
| `status` | enum | `"active"` \| `"depleted"` \| `"expired"` \| `"cancelled"` |

**POS behaviour:** If `status` is not `"active"`, or `balanceCents < tab.grandTotal`, surface an error to the operator and do not proceed to deduction. Do not block the operator from trying a different payment method.

**404 Not Found — card number not recognised**

```json
{
  "error": "gift_card_not_found"
}
```

---

## Endpoint 2 — Deduction (Redemption)

### Request

```
POST /api/v1/gift-cards/{cardNumber}/deduct
Authorization: Bearer {apiKey}
Content-Type: application/json
```

**Path parameters**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `cardNumber` | string | Yes | The ROLLER gift card number |

**Request body**

```json
{
  "amount": 41.80,
  "currency": "AUD",
  "idempotencyKey": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d-giftcard",
  "reference": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `amount` | number | Yes | Decimal float to deduct (e.g. `41.80`). Must equal tab `grandTotal` for prototype (no split payment). |
| `currency` | string | Yes | ISO 4217. Fixed `"AUD"`. |
| `idempotencyKey` | string | Yes | Recommended format: `{tabId}-giftcard`. Safe to retry. |
| `reference` | string | Yes | POS `tabId`. Stored by ROLLER for traceability. |

### Responses

**200 OK — deduction successful**

```json
{
  "transactionId": "gc_txn_7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "cardNumber": "1234567890123456",
  "amountDeducted": 41.80,
  "remainingBalance": 8.20,
  "currency": "AUD",
  "status": "success"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `transactionId` | string | ROLLER-generated. Must be stored on the `Payment` record — required to initiate a refund. |
| `amountDeducted` | number | Decimal float actually deducted (echo of request). |
| `remainingBalance` | number | Decimal float remaining on the card after deduction. |
| `status` | enum | `"success"` |

**409 Conflict — duplicate `idempotencyKey`**

```json
{
  "error": "duplicate_request",
  "transactionId": "gc_txn_7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

POS treats this as success and stores the returned `transactionId`.

**422 Unprocessable — insufficient balance**

```json
{
  "error": "insufficient_balance",
  "availableBalance": 15.00,
  "requestedAmount": 41.80
}
```

This is the race condition case (balance changed between lookup and deduction). POS surfaces error to operator; no payment record is written; tab remains open.

**422 Unprocessable — card not active**

```json
{
  "error": "card_not_active",
  "status": "expired"
}
```

**404 Not Found**

```json
{
  "error": "gift_card_not_found"
}
```

---

## Endpoint 3 — Refund

Used if a settlement needs to be reversed after a gift card deduction has already been posted (e.g. operator error, or partial system failure). Not part of the normal happy-path flow.

### Request

```
POST /api/v1/gift-cards/{cardNumber}/refund
Authorization: Bearer {apiKey}
Content-Type: application/json
```

**Path parameters**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `cardNumber` | string | Yes | The ROLLER gift card number |

**Request body**

```json
{
  "originalTransactionId": "gc_txn_7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "amount": 41.80,
  "currency": "AUD",
  "idempotencyKey": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d-giftcard-refund"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `originalTransactionId` | string | Yes | The `transactionId` returned by the deduction endpoint. |
| `amount` | number | Yes | Decimal float to refund. Must be ≤ `amountDeducted` on the original transaction. |
| `currency` | string | Yes | ISO 4217. Fixed `"AUD"`. |
| `idempotencyKey` | string | Yes | Recommended format: `{tabId}-giftcard-refund`. |

### Responses

**200 OK — refund successful**

```json
{
  "refundTransactionId": "gc_refund_abc123",
  "cardNumber": "1234567890123456",
  "amountRefunded": 41.80,
  "newBalance": 50.00,
  "currency": "AUD",
  "status": "success"
}
```

**404 Not Found — original transaction not found**

```json
{
  "error": "original_transaction_not_found"
}
```

**409 Conflict — duplicate `idempotencyKey`**

```json
{
  "error": "duplicate_request",
  "refundTransactionId": "gc_refund_abc123"
}
```

**422 Unprocessable — refund amount exceeds original**

```json
{
  "error": "refund_exceeds_original",
  "originalAmount": 41.80,
  "requestedRefund": 50.00
}
```

---

## Full Gift Card Settlement Sequence

```
Operator enters gift card number
        │
        ▼
GET  /gift-cards/{cardNumber}/balance
        │ 422 card not active / not found → show error, stop
        │ balance < tab total → show "Insufficient balance", stop
        ▼
POST /gift-cards/{cardNumber}/deduct
        │ 422 insufficient_balance (race condition) → show error, stop
        │ success → store transactionId
        ▼
POST /bookings/{uniqueId}/payments          ← existing endpoint (Giftcard paymentType — pending ROLLER update)
  { "id": "{tabId}-giftcard-payment",
    "paymentType": "Giftcard",
    "amount": {tabTotal} }
        │ failure → refund the deduction, show error, leave tab open
        ▼
POST /bookings/{uniqueId}/fnb-charges       ← new push-charges endpoint
  { lineItems: [...], paymentMethod: "gift_card" }
        │ failure → non-blocking, record RollerPushStatus = "failed", show amber badge
        ▼
DELETE /bookings/{uniqueId}/payment-lock    ← release lock
        │ failure → non-blocking, record unlockFailed flag
        ▼
Mark tab settled, display receipt
```

**Rollback rule:** If the deduction succeeds but the `/payments` post fails, the POS must call `POST /gift-cards/{cardNumber}/refund` with the stored `transactionId` before surfacing the error to the operator. The tab remains open and the lock remains.

## POS Data Storage

The `Payment` record for a gift card settlement must store:

| Field | Value |
|-------|-------|
| `type` | `"gift_card"` |
| `method` | `"gift_card"` |
| `cardNumber` | gift card number as entered (consider masking for display) |
| `amount` | tab grand total in cents |
| `status` | `"success"` or `"failed"` |
| `RollerGiftCardTransactionId` | `transactionId` from deduction response — add this column in EF migration |
| `rollerPushStatus` | `"not_pushed"` → `"pushed"` after F&B charges posted to ROLLER |

---

## Open Questions for ROLLER Team

1. **Existing endpoints:** Confirmed — ROLLER does not have gift card balance or deduction endpoints today. These three new endpoints must be built. The existing `/payments` endpoint must also be updated to accept `Giftcard` as a `paymentType`. All four changes are ROLLER team's responsibility before Phase 5 can be completed.
2. **Partial refund:** Is a partial refund (refunding less than the full deduction amount) in scope? Current design allows it (`amountCents ≤ originalAmount`) but the prototype won't exercise this path.
3. **Card number format:** What format do ROLLER gift card numbers take? (Length, character set, any check digit.) This affects input validation in the POS UI.
4. **Expired card behaviour:** Should an expired card return `404` or a `200` with `status: "expired"`? The current design returns `200` with status — confirm preference.
5. **Refund window:** Is there a time limit after which a deduction cannot be refunded?

---

## Sign-off

| Reviewer | Role | Date | Approved |
|----------|------|------|----------|
| | ROLLER Backend | | |
| | POS Lead | | |
