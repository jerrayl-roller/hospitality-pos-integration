# Push F&B Charges — New ROLLER Endpoint Contract

**Status:** DRAFT — pending ROLLER team review and approval  
**Phase:** Designed in Phase 0 (T0.5) · Integrated by POS in Phase 6 (T6.1)  
**Dependency:** Review the existing "Add Transaction Record" endpoint first (`existing-endpoints.md` §5). If that endpoint already supports line-level F&B detail, this new endpoint may be unnecessary — confirm before building.

---

## Overview

Posts F&B line items and a payment record against a ROLLER booking after a POS tab is settled. This is the primary reconciliation mechanism — it is how ROLLER's booking record is brought into sync with what was charged at the POS.

**Key design constraints:**
- Must support line-level detail (individual products, quantities, prices) — a single-total push is insufficient for ROLLER-side reporting.
- Caller-generated `idempotencyKey` makes the endpoint safe to retry after network failure.
- Only F&B items **added during the visit** (from `tab.addedItemsJson`) are pushed here. Items already on the booking at import time (`tab.importedItemsJson`) are not re-posted.
- Called after successful payment and before displaying the receipt.
- Non-blocking on failure: if the push fails, the tab is still marked settled and the operator sees an amber "ROLLER Sync Pending" badge. A retry is available.
- **Amount format:** Use **decimal float** (e.g. `4.18`), not cents integers. The existing ROLLER `/payments` endpoint uses decimal floats; this new endpoint should follow the same convention for consistency. The POS backend stores amounts internally in cents and converts on the way out.

---

## Endpoint

### Request

```
POST /api/v1/bookings/{bookingId}/fnb-charges
Authorization: Bearer {apiKey}
Content-Type: application/json
```

**Path parameters**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `bookingId` | string | Yes | The ROLLER booking identifier |

**Request body**

```json
{
  "tabId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "idempotencyKey": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d-settlement",
  "lineItems": [
    {
      "productId": "prod_abc123",
      "productName": "Craft Beer",
      "quantity": 2,
      "unitPriceExclGst": 10.00,
      "unitPriceInclGst": 11.00,
      "gstAmount": 1.00,
      "lineTotal": 22.00
    },
    {
      "productId": "prod_def456",
      "productName": "Fish & Chips",
      "quantity": 1,
      "unitPriceExclGst": 18.00,
      "unitPriceInclGst": 19.80,
      "gstAmount": 1.80,
      "lineTotal": 19.80
    }
  ],
  "paymentMethod": "card",
  "paymentReference": "XXXX-XXXX-XXXX-4242",
  "totalAmountInclGst": 41.80,
  "currency": "AUD",
  "settledAt": "2026-04-20T04:15:00Z"
}
```

**Field definitions**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `tabId` | string | Yes | POS tab UUID. Stored by ROLLER for traceability. |
| `idempotencyKey` | string | Yes | Stable, caller-generated key. Recommended format: `{tabId}-settlement`. Duplicate requests with the same key return the original response without re-posting. |
| `lineItems` | array | Yes | One entry per distinct product. Must not be empty. |
| `lineItems[].productId` | string | Yes | ROLLER product ID (from the catalogue). |
| `lineItems[].productName` | string | Yes | Product display name at time of sale (snapshot — not re-fetched). |
| `lineItems[].quantity` | integer | Yes | Must be > 0. |
| `lineItems[].unitPriceExclGst` | number | Yes | Decimal float (e.g. `10.00`). Price per unit excluding GST. |
| `lineItems[].unitPriceInclGst` | number | Yes | Decimal float. Price per unit including GST (= `unitPriceExclGst` × 1.1 for Australian 10% GST). |
| `lineItems[].gstAmount` | number | Yes | Decimal float. GST per unit (= `unitPriceInclGst` − `unitPriceExclGst`). Rounded to 2 decimal places. |
| `lineItems[].lineTotal` | number | Yes | Decimal float. `unitPriceInclGst` × `quantity`. Rounded to 2 decimal places. |
| `paymentMethod` | enum | Yes | `"card"` \| `"gift_card"` |
| `paymentReference` | string | Yes | Masked card number (`XXXX-XXXX-XXXX-{last4}`) or gift card number. PII — ROLLER must store appropriately. |
| `totalAmountInclGst` | number | Yes | Decimal float. Sum of all `lineTotal` values. Must equal sum for server-side validation (allow ±0.01 tolerance for floating-point rounding). |
| `currency` | string | Yes | ISO 4217. Fixed `"AUD"` for prototype. |
| `settledAt` | ISO 8601 | Yes | Timestamp of settlement on the POS side. |

### Responses

**200 OK — charges posted**

```json
{
  "chargeId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "bookingId": "string",
  "tabId": "string",
  "status": "posted",
  "postedAt": "2026-04-20T04:15:01Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `chargeId` | UUID | ROLLER-generated identifier for this charge record. Store on `Payment.RollerChargeId` for reconciliation. |
| `status` | enum | `"posted"` |
| `postedAt` | ISO 8601 | Server-side timestamp. |

**409 Conflict — duplicate `idempotencyKey`**

```json
{
  "error": "duplicate_request",
  "chargeId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

The POS treats this as success and stores the returned `chargeId`.

**422 Unprocessable — total mismatch**

```json
{
  "error": "total_mismatch",
  "detail": "Sum of lineItem totals (4380) does not equal totalAmountInclGst (4180)"
}
```

**404 Not Found — booking does not exist**

```json
{
  "error": "booking_not_found"
}
```

---

## POS Behaviour on Failure

| HTTP status | POS action |
|-------------|-----------|
| 200 | Set `Payment.RollerPushStatus = "pushed"`. Show green "Synced to ROLLER" badge. |
| 409 (duplicate) | Treat as success. Set `RollerPushStatus = "pushed"`. |
| 4xx (other) | Set `RollerPushStatus = "failed"`. Log full request payload for manual retry. Show amber "ROLLER Sync Pending" badge. Do NOT fail or reverse the settlement. |
| 5xx / network error | Same as 4xx above. |

Retry is available via `POST /api/tabs/{tabId}/retry-push` in the POS admin. The same `idempotencyKey` is reused, making the retry safe.

---

## Open Questions for ROLLER Team

1. **Existing "Add Transaction Record" overlap:** Review whether `/add-transaction-record` already covers line-level F&B posting. If it does, this new endpoint should be dropped and the existing one enhanced instead. Confirm in Phase 0 (T0.1 / T0.5).
2. **Booking state after push:** Should posting charges automatically update the booking's payment status in ROLLER, or does that require a separate `PUT /bookings/{id}` call? (See Phase 6, T6.2.)
3. **Partial post:** If the POS crashes after posting half the line items, can the idempotency key still protect a full retry? (Assumed yes — the entire request is idempotent as a unit.)
4. **Imported items:** Should items already on the booking at import time (`importedItemsJson`) also be re-posted here, or does ROLLER already have them? (Current assumption: imported items are NOT re-posted.)

---

## Sign-off

| Reviewer | Role | Date | Approved |
|----------|------|------|----------|
| | ROLLER Backend | | |
| | POS Lead | | |
