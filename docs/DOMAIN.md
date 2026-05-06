# ROLLER F&B POS — Domain Reference

**Generated:** 2026-04-22  
**Covers:** Phases 1–6 (prototype complete)  
**Stack:** Angular 19 (standalone) · .NET 9 Web API · SQL Server + EF Core · Angular Material

---

## Contents

1. [Architecture Overview](#architecture-overview)
2. [Data Models](#data-models)
3. [POS API Endpoints](#pos-api-endpoints)
4. [ROLLER API Calls](#roller-api-calls)
5. [Phase-by-Phase Summary](#phase-by-phase-summary)
6. [Divergences from Implementation Plan](#divergences-from-implementation-plan)

---

## Architecture Overview

```
Browser (Angular 19)
    └── ApiService (HTTP)
            └── .NET 9 Web API  (/api/*)
                    ├── SQL Server (EF Core) — tabs, payments
                    └── IRollerApiClient (Bearer token, auto-refresh)
                                └── ROLLER API (api.roller.app)
```

The POS backend is the single integration point with ROLLER. The Angular frontend never calls ROLLER directly. All ROLLER calls go through `IRollerApiClient`, which handles token acquisition (`RollerTokenService`), refresh on 401, and throws typed `RollerApiException` on failure.

---

## Data Models

### `Tab`

| Column | Type | Description |
|--------|------|-------------|
| `TabId` | `Guid` PK | |
| `BookingUniqueId` | `string?` | ROLLER booking `uniqueId`; null for walk-in tabs |
| `BookingReference` | `string?` | Human-readable booking reference |
| `GuestName` | `string?` | |
| `GuestEmail` | `string?` | |
| `GuestPhone` | `string?` | |
| `ImportedItemsJson` | `string` | JSON array of `TabLineItem` — items from the original ROLLER booking |
| `AddedItemsJson` | `string` | JSON array of `TabLineItem` — items added in this POS session |
| `AuditLogJson` | `string` | JSON array — conflict / change log |
| `GrandTotal` | `decimal(18,4)` | Sum of all imported + added item totals |
| `PreAuthCardNumber` | `string?` | Simulated card number (`XXXX-XXXX-XXXX-XXXX`) |
| `PreAuthStatus` | `string` | `"none"` \| `"simulated"` |
| `PaymentStatus` | `string` | `"pending_lock"` → `"open"` → `"complete"` \| `"errored"` |
| `HasPendingConflict` | `bool` | Set during booking resync if a conflict is detected |
| `OpenedAt` | `DateTime` | UTC |
| `SettledAt` | `DateTime?` | UTC; set when status transitions to `complete` or `errored` |

**`TabLineItem` (JSON record within columns above):**
```json
{ "productId": "123", "productName": "Sparkling Water", "quantity": 2, "unitPrice": 5.00 }
```

---

### `Payment`

| Column | Type | Description |
|--------|------|-------------|
| `PaymentId` | `Guid` PK | |
| `TabId` | `Guid` FK → `Tab` | Cascade delete |
| `Type` | `string` | `"pre_auth"` \| `"payment"` \| `"booking_payment"` |
| `Method` | `string` | `"visa"` \| `"mastercard"` \| `"amex"` \| `"cash"` \| `"gift_card"` |
| `CardNumber` | `string?` | Simulated card (`XXXX-XXXX-XXXX-XXXX`), gift card number, or ROLLER transaction ID |
| `Amount` | `decimal(18,4)` | |
| `Currency` | `string` | `"AUD"` |
| `Status` | `string` | `"success"` |
| `IsTip` | `bool` | Tip records are separate rows; not pushed individually to ROLLER |
| `RollerPushStatus` | `string` | `"not_pushed"` \| `"pushed"` \| `"failed"` \| `"not_applicable"` |
| `RollerGiftCardTransactionId` | `string?` | Returned by ROLLER on gift card deduction |
| `CreatedAt` | `DateTime` | UTC |

**Payment type guide:**
- `pre_auth` — simulated pre-authorisation created on booking import; not synced to ROLLER
- `booking_payment` — payment that already existed in ROLLER when the booking was imported; not re-synced
- `payment` — payment taken in this POS session; synced to ROLLER on settlement

---

## POS API Endpoints

### Products

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/products` | Returns filtered F&B catalogue from ROLLER (cached 30 min) |

**Response:** `ProductDto[]`
```json
[
  {
    "productId": "123",
    "name": "Sparkling Water",
    "parentName": "Beverages",
    "price": 5.00,
    "productType": "AddOn",
    "productSubType": "Stock",
    "category": "Beverages",
    "imageUrl": null
  }
]
```

---

### Bookings

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/bookings/search?q={query}` | Search ROLLER bookings (min 3 chars) |
| `GET` | `/api/guests/{customerId}` | Fetch guest details from ROLLER |

**`/api/bookings/search` response:** `BookingSummaryDto[]`
```json
[
  {
    "bookingUniqueId": "abc-123",
    "bookingReference": "BK-001",
    "guestName": "Jane Smith",
    "bookingDate": "2026-04-22",
    "status": "confirmed",
    "totalAmount": 150.00,
    "lineItemCount": 3,
    "items": [{ "productName": "Session Pass", "quantity": 2 }],
    "customerId": 42,
    "isImported": false
  }
]
```

---

### Tabs

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/tabs` | Create a walk-in tab |
| `GET` | `/api/tabs` | List all tabs (summary) |
| `GET` | `/api/tabs/{tabId}` | Get full tab detail |
| `DELETE` | `/api/tabs/{tabId}` | Delete an empty open tab |
| `POST` | `/api/tabs/from-booking` | Import a ROLLER booking as a tab |
| `POST` | `/api/tabs/{tabId}/items` | Add an item to the tab |
| `PUT` | `/api/tabs/{tabId}/items` | Restore/replace all items (bulk) |
| `DELETE` | `/api/tabs/{tabId}/items/{productId}` | Remove an item |
| `POST` | `/api/tabs/{tabId}/payments` | Add a payment (card, cash, or gift card) |
| `POST` | `/api/tabs/{tabId}/settle` | Settle the tab and sync to ROLLER |
| `POST` | `/api/tabs/{tabId}/retry-sync` | Re-attempt ROLLER sync for an `errored` tab |
| `GET` | `/api/tabs/{tabId}/receipt` | Get receipt data |

**`POST /api/tabs/from-booking` request:**
```json
{
  "bookingUniqueId": "abc-123",
  "guestName": "Jane Smith",
  "guestEmail": "jane@example.com",
  "guestPhone": "+61400000000"
}
```

**`POST /api/tabs/{tabId}/payments` request:**
```json
{
  "method": "new_card",
  "amount": 45.00,
  "tipAmount": 5.00,
  "giftCardNumber": null
}
```
Methods: `pre_auth_card` \| `new_card` \| `cash` \| `gift_card`

---

### Admin

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/admin/resync-products` | Invalidate product cache |
| `POST` | `/api/admin/resync-bookings` | Pull fresh booking state from ROLLER for all open tabs |
| `DELETE` | `/api/admin/tabs/locks` | Force-release ROLLER payment locks for all booking-linked tabs |
| `POST` | `/api/admin/clear-data` | Delete all tabs and payments (dev/demo use only) |

---

## ROLLER API Calls

All calls authenticated with `Authorization: Bearer {token}` (OAuth2 client credentials, auto-refreshed on 401).

---

### Pre-existing ROLLER Endpoints

#### `GET /data/products?pageNumber={n}&pageSize=100`

Called by: `ProductService` (Phase 1)  
Paginated — fetches all pages until an empty page is returned.

**Response shape (normalised across response variants):**
```json
[
  {
    "productId": "123",
    "parentProductId": "100",
    "name": "Sparkling Water",
    "price": 5.00,
    "productType": "AddOn",
    "productSubType": "Stock",
    "productStatus": "Published",
    "reportingCategoryName": "Beverages",
    "imageUrl": null
  }
]
```

**Filter applied by the POS:**
- `productType == "AddOn"`
- `productStatus == "Published"`
- `productSubType == "Stock"`
- Must have a `parentProductId` (variant products only; parent products are excluded from the catalogue but used for display name resolution)

---

#### `GET /bookings?keywords={query}`

Called by: `BookingService.SearchBookingsAsync` (Phase 2)

**Response shape:**
```json
{
  "bookings": [
    {
      "uniqueId": "abc-123",
      "bookingReference": "BK-001",
      "name": "Jane Smith",
      "status": "confirmed",
      "total": 150.00,
      "customerId": 42,
      "items": [
        {
          "productId": 456,
          "quantity": 2,
          "cost": 10.00,
          "bookingDate": "2026-04-22"
        }
      ],
      "payments": [
        {
          "paymentMethod": "CreditCard",
          "total": 50.00,
          "transactionId": "txn-abc",
          "createdDate": "2026-04-01T10:00:00Z"
        }
      ]
    }
  ]
}
```

---

#### `GET /bookings/{uniqueId}`

Called by: `BookingService.ImportBookingAsync` (Phase 2) and `BookingResyncService` (Phase 4)

**Key fields read:**
- `remainder` — if < 0.01, booking is fully prepaid; import blocked
- `items[]` — each with `productId`, `quantity`, `cost`
- `payments[]` — existing payments imported as `booking_payment` records
- `bookingReference`, `name`

---

#### `GET /guests/{customerId}`

Called by: `BookingService.GetGuestDetailsAsync` (Phase 2)

**Response:**
```json
{ "firstName": "Jane", "lastName": "Smith", "email": "jane@example.com", "phone": "+61400000000" }
```

---

#### `PUT /bookings/{uniqueId}`

Called by: `RollerSyncService.PushItemsAsync` (Phase 6)  
Only called if the tab has items in `AddedItemsJson` (items added during the POS session). Items that were on the booking at import time (`ImportedItemsJson`) are **not** re-sent.

**Request body:**
```json
{
  "newItems": [
    {
      "productId": 123,
      "quantity": 2,
      "bookingDate": "2026-04-22",
      "priceOverride": null
    }
  ]
}
```

**Notes:**
- `productId` is sent as `int` (ROLLER requirement); stored as `string` in the POS
- `bookingDate` is the current UTC date (`yyyy-MM-dd`)
- Response body is not consumed

---

#### `GET /giftcards/{cardNumber}/balance`

Called by: `RollerGiftCardService.CheckBalanceAsync` (Phase 5)

**Response:**
```json
{ "exists": true, "balance": 50.00, "expired": false }
```

**Error handling:**
- `exists == false` → `gift_card_not_found`
- `expired == true` → `gift_card_expired`
- `balance < requiredAmount` → `gift_card_insufficient_balance`

---

#### `POST /giftcards/{cardNumber}/deduct`

Called by: `RollerGiftCardService.DeductAsync` (Phase 5)

**Request body:**
```json
{ "amount": 45.00, "transactionId": "{tabId}", "bookingUniqueId": "{bookingUniqueId}" }
```

**Response:**
```json
{ "transactionId": "roller-txn-abc", "amount": 45.00, "balance": 5.00 }
```

**Error handling (409 Conflict — FluentValidation format):**
```json
[{ "errorCode": "GIFT_CARD_INSUFFICIENT_BALANCE", "errorMessage": "..." }]
```

Idempotency: `GIFT_CARD_TRANSACTION_ID_ALREADY_USED` is treated as success — the prior deduction went through.

---

### New ROLLER Endpoints

> These endpoints did not exist in ROLLER prior to this project. They were designed and built as part of the prototype integration.

---

#### ⭐ `POST /bookings/{uniqueId}/payment-lock`

Called by: `PaymentLockService.AcquireLockAsync` (Phase 3)  
Locks the booking for payment in ROLLER, preventing concurrent settlement from other systems.

**Request body:** `{}` (empty)

**Success:** any 2xx response  
**Failure:** any non-2xx → `PaymentLockFailedException` → POS returns 503 to client; pending tab is rolled back

**Notes from plan vs. implementation:** The designed contract included `lockId`, `lockedBySystem`, `lockedByReference`, and `reason` fields. The implementation calls with an empty body. The ROLLER-side contract may include these fields — the POS does not send them in the prototype.

---

#### ⭐ `DELETE /bookings/{uniqueId}/payment-lock`

Called by: `PaymentLockService.ReleaseLockAsync` (Phase 3, Phase 6)  
Releases the payment lock acquired at booking import.

**Request body:** none  
**404 response:** treated as already-released (idempotent)

Called as the third step of the settlement sync flow. If this call fails, the tab moves to `"errored"` state — the lock will expire naturally or can be force-released via `DELETE /api/admin/tabs/locks`.

---

#### ⭐ `POST /bookings/{uniqueId}/payments`

Called by: `RollerSyncService.PushPaymentsAsync` (Phase 6)  
Records the POS payment against the ROLLER booking.

**Request body:**
```json
{
  "id": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
  "paymentType": 1,
  "amount": 50.00,
  "tip": 5.00,
  "tipNote": null,
  "cardLast4Digits": "4242",
  "paymentBrand": "visa"
}
```

**Field notes:**
- `id`: `payment.PaymentId.ToString("N")` — 32-character hex, no hyphens
- `paymentType` enum:

  | Value | Int |
  |-------|-----|
  | CreditCard | 1 |
  | CreditCardPreAuth | 2 |
  | Cash | 3 |
  | Giftcard | 7 |
  | Other | 6 |

- POS method → ROLLER paymentType mapping:

  | POS `Method` | ROLLER `paymentType` |
  |---|---|
  | `visa`, `mastercard`, `amex` | `1` (CreditCard) |
  | `cash` | `3` (Cash) |
  | `gift_card` | `7` (Giftcard) |
  | other | `6` (Other) |

- `amount`: includes tip (`payment.Amount + tipTotal`)
- `tip`: tip amount only; `null` if no tip
- `paymentBrand`: card type string (`"visa"` etc.) for card payments; `null` for cash/gift card
- `cardLast4Digits`: last 4 of the POS card number (segment after final `-`)

**Exclusions — payments NOT sent to ROLLER:**
- `Type == "pre_auth"` — simulated pre-authorisation; not a real charge
- `Type == "booking_payment"` — already existed in ROLLER at import time
- `IsTip == true` — tips are attached to the parent payment's `tip` field, not sent as separate records
- `RollerPushStatus == "pushed"` — already synced (relevant on retry)

**Response body:** not consumed  
**On success:** `payment.RollerPushStatus = "pushed"`  
**On failure:** `payment.RollerPushStatus = "failed"`

---

## Phase-by-Phase Summary

### Phase 1 — Scaffold, Product Catalogue, Tab Management

**What was built:**
- .NET 9 Web API + EF Core + SQL Server with `Tab` and `Payment` entities
- Angular 19 standalone app with Angular Material
- `IRollerApiClient` — transport wrapper with Bearer auth, token refresh, typed exceptions
- `GET /data/products` called with pagination; cached 30 minutes
- F&B filter: `AddOn` + `Published` + `Stock` + must have `parentProductId`
- Tab CRUD: create, list, get, add/remove items, delete
- Running grand total recomputed on every item change

**ROLLER endpoints used:**
- `GET /data/products` (paginated)

---

### Phase 2 — Booking Search, Import, Pre-Auth Simulation

**What was built:**
- `GET /api/bookings/search?q=` — proxies `GET /bookings?keywords=` to ROLLER; min 3-char guard
- `GET /api/guests/{customerId}` — fetches guest contact details from ROLLER
- `POST /api/tabs/from-booking` — imports a booking as a tab:
  - Fetches booking detail from ROLLER
  - Guards against: fully prepaid (`remainder < 0.01`), duplicate import (same `bookingUniqueId`)
  - Maps `items[]` → `ImportedItemsJson` using product name lookup
  - Imports existing `payments[]` as `booking_payment` records
  - Generates a simulated pre-auth card (random `XXXX-XXXX-XXXX-XXXX`, random card type)
  - Creates a `pre_auth` payment record

**ROLLER endpoints used:**
- `GET /bookings?keywords={q}`
- `GET /bookings/{uniqueId}`
- `GET /guests/{customerId}`

---

### Phase 3 — Payment Lock

**What was built:**
- `POST /bookings/{uniqueId}/payment-lock` called during `ImportBookingAsync` after creating the pending tab record
- On lock failure: pending tab and its payments are rolled back; 503 returned to client
- `DELETE /api/admin/tabs/locks` — force-releases locks for all booking-linked tabs (escape hatch)

**ROLLER endpoints used:**
- `POST /bookings/{uniqueId}/payment-lock` ⭐ new
- `DELETE /bookings/{uniqueId}/payment-lock` ⭐ new

---

### Phase 4 — Booking Resync (simplified from webhook design)

**What was built:**
- `BookingResyncService` — pull-based resync triggered manually via `POST /api/admin/resync-bookings`
- Re-fetches each open booking-linked tab's booking from ROLLER; diffs items and payments
- Flags tabs as `errored` if new booking-level payments are detected since import
- `HasPendingConflict` set on the `Tab` record when item conflicts are found
- `TabHub` (SignalR) scaffolded in Program.cs (wired but not triggered in this phase)

**ROLLER endpoints used:**
- `GET /bookings/{uniqueId}` (per open tab)

> **See [Divergences](#divergences-from-implementation-plan):** Live webhook push not implemented; replaced with manual admin-triggered pull resync.

---

### Phase 5 — Payment, Settlement, and Receipt

**What was built:**
- `POST /api/tabs/{tabId}/payments` — multi-step payment accumulation:
  - Methods: `pre_auth_card` (uses stored pre-auth card), `new_card` (generates random card), `cash`, `gift_card`
  - Gift card: calls balance check then deduction; `RollerGiftCardTransactionId` stored
  - Tip is a separate `Payment` row with `IsTip = true`, `RollerPushStatus = "not_applicable"`
  - Validates amount does not exceed outstanding balance
- `POST /api/tabs/{tabId}/settle` — validates fully paid, then triggers ROLLER sync (Phase 6 flow)
- `GET /api/tabs/{tabId}/receipt` — returns formatted tax invoice data with GST breakdown (10%)

**ROLLER endpoints used:**
- `GET /giftcards/{cardNumber}/balance`
- `POST /giftcards/{cardNumber}/deduct`
- `DELETE /bookings/{uniqueId}/payment-lock` (called during settle)

> **See [Divergences](#divergences-from-implementation-plan):** Settlement uses a two-step flow (add payments, then settle) rather than the single-call design in the plan.

---

### Phase 6 — ROLLER Sync on Settlement

**What was built:**
- `RollerSyncService` — executes three sequential ROLLER calls on settlement:
  1. `PUT /bookings/{uniqueId}` — push added items (skipped if `AddedItemsJson` is empty)
  2. `POST /bookings/{uniqueId}/payments` — record each POS payment (skips pre_auth, booking_payment, tips, already-pushed)
  3. `DELETE /bookings/{uniqueId}/payment-lock` — release the lock
- Any failure marks tab `PaymentStatus = "errored"` and saves `Payment.RollerPushStatus = "failed"`
- `POST /api/tabs/{tabId}/retry-sync` — re-runs all three calls for an `errored` tab; already-pushed payments are skipped
- Frontend: `"complete"` (blue) and `"errored"` (red) status badges; Retry Sync button; error banner in receipt flyout
- Tabs without a `BookingUniqueId` skip all ROLLER calls and go directly to `"complete"`

**Terminal tab states:** `"complete"` (all syncs succeeded) \| `"errored"` (sync failed — payment was taken, ROLLER state may be partial)

**ROLLER endpoints used:**
- `PUT /bookings/{uniqueId}` (existing endpoint, new usage)
- `POST /bookings/{uniqueId}/payments` ⭐ new
- `DELETE /bookings/{uniqueId}/payment-lock` ⭐ new (same as Phase 3)

> **See [Divergences](#divergences-from-implementation-plan):** Uses `PUT /bookings/{id}` + `POST /bookings/{id}/payments` rather than the designed `POST /bookings/{id}/fnb-charges` endpoint.

---

## Divergences from Implementation Plan

### 1. Phase 4: Webhooks replaced with pull-based resync

**Plan:** Live webhook endpoint (`POST /api/webhooks/roller`) with HMAC validation, SignalR push to browser, ngrok tunnel.

**Built:** `BookingResyncService` — a manually-triggered admin operation (`POST /api/admin/resync-bookings`) that polls ROLLER for each open booking-linked tab. No inbound webhook endpoint. `TabHub` (SignalR) exists in Program.cs but is not triggered by resync.

**Impact:** Real-time conflict notification not implemented. The resync must be triggered manually by an admin. For the prototype demo, this is acceptable.

---

### 2. Phase 5: Two-step payment flow (not single-call settle)

**Plan:** `POST /api/tabs/{tabId}/settle/card` and `POST /api/tabs/{tabId}/settle/gift-card` as separate single-call endpoints that collect payment and settle atomically.

**Built:** `POST /api/tabs/{tabId}/payments` (add one payment at a time, supports all methods) + `POST /api/tabs/{tabId}/settle` (settles once fully paid). This supports partial payment accumulation and multi-method payment.

---

### 3. Phase 6: `fnb-charges` endpoint not used

**Plan:** `POST /bookings/{id}/fnb-charges` — a bespoke new endpoint designed to carry itemised F&B line items, idempotency key, and payment summary in one call.

**Built:** Two existing/new ROLLER endpoints used instead:
- `PUT /bookings/{uniqueId}` with `newItems` — adds F&B items to the booking
- `POST /bookings/{uniqueId}/payments` — records the payment

The `fnb-charges` designed contract (`docs/api-contracts/push-charges.md`) was not implemented. The equivalent function is split across these two calls.

---

### 4. Payment lock contract simplified

**Plan:** Lock request body included `lockedBySystem`, `lockedByReference`, `reason`; unlock body included `lockId` and `reason`. Lock response included a `lockId` to be stored on the tab.

**Built:** Lock called with empty body `{}`; unlock called with no body. No `lockId` stored. The POS does not parse the lock response.

---

### 5. Terminal settlement state renamed

**Plan:** Terminal state was `"settled"`.

**Built:** `"complete"` (sync succeeded) and `"errored"` (sync failed). The string `"settled"` does not appear as a terminal state in the current codebase; `"errored"` is a new state not in the original plan.

---

### 6. Admin lock endpoint scope changed

**Plan:** `DELETE /api/admin/tabs/{tabId}/lock` — force-release a single tab's lock, protected by admin token header.

**Built:** `DELETE /api/admin/tabs/locks` — releases locks for all booking-linked tabs in a single call; no admin token guard (prototype only).
