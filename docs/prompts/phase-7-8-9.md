# Phases 7–9 Implementation Plan

**Produced:** 2026-04-22  
**Prototype baseline:** Phases 1–6 complete. See `docs/DOMAIN.md` for the full endpoint and data-model reference.

---

## Open Questions — Stated Assumptions

The following unresolved questions are answered with explicit assumptions before the phase breakdown. Each assumption that constrains design is marked and must be confirmed with the ROLLER team before dependent code is written.

### OQ-A — Gift card refund: what is the ROLLER endpoint path and idempotency mechanism?

**Assumption:** The gift card refund endpoint follows the same path convention as deduction:
```
POST /giftcards/{cardNumber}/refund
Body: { "originalTransactionId": "...", "amount": 45.00, "idempotencyKey": "uuid" }
```
The `originalTransactionId` is the value returned by the deduction call and stored in `Payment.RollerGiftCardTransactionId`. Duplicate idempotency keys return a 409 with `GIFT_CARD_TRANSACTION_ID_ALREADY_USED` (same as deduction — treat as success).

**Confirm before Phase 7a:** exact path, body fields, 409 error code format.

---

### OQ-B — ROLLER booking response: how are discounts represented?

**Assumption:** The booking detail response (`GET /bookings/{uniqueId}`) includes a top-level `discounts` array:
```json
{
  "discounts": [
    {
      "discountId": "string",
      "name": "10% Loyalty Discount",
      "type": "percentage",
      "value": 10.0,
      "amount": 15.00
    }
  ]
}
```
If ROLLER instead represents discounts as negative-cost items inside `items[]`, the import logic must be adapted to detect and separate them. Both representations are handled in Phase 8.

**Confirm before Phase 8:** exact field names, whether `type` is `"percentage"` / `"fixed"` / `"voucher"`, and whether `amount` is always pre-computed.

---

### OQ-C — ROLLER booking update: does `PUT /bookings/{id}` accept a `discounts` field?

**Assumption:** The update endpoint does not currently accept discounts. The existing `newItems` body will be extended to optionally carry a `discounts` array:
```json
{ "newItems": [...], "discounts": [...] }
```
If ROLLER does not support this, discounts applied in the POS are recorded locally only and noted as a gap. A separate `POST /bookings/{id}/discounts` endpoint may need to be designed (out of scope for Phase 8 unless confirmed available).

**Confirm before Phase 8 sync implementation.**

---

### OQ-D — ROLLER product API: how are modifiers represented?

**Assumption:** Modifiers (also called "add-ons" or "options" in some systems) are returned as part of the product object in `GET /data/products`:
```json
{
  "productId": "123",
  "modifierGroups": [
    {
      "groupId": "grp-1",
      "groupName": "Size",
      "required": true,
      "multiSelect": false,
      "modifiers": [
        { "modifierId": "456", "name": "Regular", "priceAdjustment": 0.00 },
        { "modifierId": "457", "name": "Large",   "priceAdjustment": 1.50 }
      ]
    }
  ]
}
```
If modifiers are returned via a separate endpoint, `GET /data/products` is called first and modifier groups are fetched lazily as needed.

**Confirm before Phase 9:** exact field names, whether `priceAdjustment` is a signed delta (positive = surcharge, negative = reduction), and whether required groups exist.

---

### OQ-E — ROLLER booking update: how are modifiers represented in `newItems`?

**Assumption:** Each item in the `newItems` array can carry a `modifiers` sub-array of selected modifier IDs:
```json
{
  "productId": 123,
  "quantity": 1,
  "bookingDate": "2026-04-22",
  "modifiers": [{ "modifierId": 457 }],
  "priceOverride": null
}
```
If ROLLER does not support modifiers in `newItems`, the fallback is to pass `priceOverride` with the effective price (base + modifier adjustments) so the booking total is correct, and store the modifier detail locally only.

**Confirm before Phase 9 sync implementation.**

---

## Phase 7a — POS-Side Refunds

### Goal

Allow operators to issue refunds against payments taken in the current system. Payments that originated in ROLLER (`booking_payment` type) cannot be refunded here and display a clear message directing staff to ROLLER. Gift card refunds call the ROLLER gift card refund endpoint to restore the balance.

---

### What Already Exists

- `Payment.Type` distinguishes `"payment"` (POS-taken), `"booking_payment"` (imported from ROLLER), and `"pre_auth"` (simulated) — drives which records are eligible.
- `Payment.RollerGiftCardTransactionId` stores the transaction ID returned by ROLLER deduction — required for gift card refund idempotency.
- `IRollerGiftCardService` already has `CheckBalanceAsync` and `DeductAsync` — `RefundAsync` is added in this phase.
- Tab terminal states are `"complete"` and `"errored"` — refunds are only permitted on these states.

---

### Database Changes

#### New entity: `Refund`

```csharp
public class Refund
{
    public Guid RefundId { get; set; }
    public Guid TabId { get; set; }
    public Tab Tab { get; set; } = null!;
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";            // mirrors parent Payment.Method
    public string? CardNumber { get; set; }             // mirrors parent Payment.CardNumber
    public string? Reason { get; set; }
    public string RollerRefundPushStatus { get; set; } = "not_pushed";
                                                        // not_pushed | pushed | failed | not_applicable
    public string? RollerGiftCardRefundTransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

#### Changes to `Payment`

Add:
```csharp
public string RefundStatus { get; set; } = "none";
// none | partial | refunded | not_refundable
```

`not_refundable` is set at Payment creation for `Type == "pre_auth"` and `Type == "booking_payment"`.

#### EF migration

`dotnet ef migrations add AddRefunds`

- `Refunds` table with PK `RefundId`; FK `PaymentId → Payments` (restrict delete); FK `TabId → Tabs` (cascade)
- `Payment.RefundStatus` column (default `"none"`)
- On migration: set `RefundStatus = "not_refundable"` for existing `pre_auth` and `booking_payment` records

---

### Backend Changes

#### `IRollerGiftCardService` — add `RefundAsync`

```csharp
Task<(string? transactionId, string? error)> RefundAsync(
    string giftCardNumber, decimal amount, Guid refundId,
    string originalTransactionId, CancellationToken ct = default);
```

- Calls `POST /giftcards/{cardNumber}/refund`
- Request body: `{ "originalTransactionId": "...", "amount": amount, "idempotencyKey": refundId.ToString("N") }`
- Response: maps `transactionId` from response
- Handles 409 `GIFT_CARD_TRANSACTION_ID_ALREADY_USED` as success (idempotent)
- Error codes returned: `gift_card_refund_failed`, `gift_card_not_found`

---

#### New `RefundService`

```csharp
public interface IRefundService
{
    Task<(TabDto? tab, string? error, string? detail)> ProcessRefundAsync(
        Guid tabId, Guid paymentId, ProcessRefundRequest req, CancellationToken ct = default);
    Task<(TabDto? tab, string? error)> RetrySyncAsync(
        Guid tabId, Guid refundId, CancellationToken ct = default);
}
```

**`ProcessRefundAsync` flow:**

1. Load tab with payments and refunds
2. Validate tab status is `"complete"` or `"errored"` — else `tab_not_settled`
3. Load the target payment; validate:
   - `payment.Type == "payment"` — else `not_refundable` (with `detail` explaining booking_payment must be refunded in ROLLER)
   - `req.Amount > 0` — else `invalid_amount`
   - `req.Amount ≤ (payment.Amount − existingRefundsTotal)` — else `exceeds_refundable_amount`
4. Create a `Refund` record in memory (`RollerRefundPushStatus = "not_pushed"`)
5. If `payment.Method == "gift_card"`:
   - Call `rollerGiftCardService.RefundAsync(...)` using `payment.RollerGiftCardTransactionId` as `originalTransactionId`
   - On success: set `refund.RollerRefundPushStatus = "not_pushed"` (ROLLER booking sync comes in 7b), store `RollerGiftCardRefundTransactionId`
   - On failure: set `refund.RollerRefundPushStatus = "failed"`, store the refund anyway, return error
6. Update `payment.RefundStatus`:
   - If `refund.Amount == payment.Amount − priorRefunds`: `"refunded"`
   - Else: `"partial"`
7. Save and return updated `TabDto`

**Eligibility rule summary:**

| Payment type | Refundable? | Reason |
|---|---|---|
| `payment` (card/cash) | Yes — simulated | No real payment gateway; refund recorded only |
| `payment` (gift card) | Yes — calls ROLLER | `POST /giftcards/{card}/refund` |
| `booking_payment` | No | Must be refunded in ROLLER directly |
| `pre_auth` | No | Not a real charge |
| Tip (`IsTip == true`) | No | Tips are not refunded independently |

---

#### `TabsController` — new endpoints

```
POST   /api/tabs/{tabId}/payments/{paymentId}/refund   — issue a refund
GET    /api/tabs/{tabId}/refunds                       — list refunds for a tab
POST   /api/tabs/{tabId}/refunds/{refundId}/retry-sync — retry ROLLER sync for a failed refund
```

**`POST /api/tabs/{tabId}/payments/{paymentId}/refund` request:**
```json
{ "amount": 45.00, "reason": "Customer request" }
```

**Response:** `TabDto` (extended — see DTOs below)

**Error responses:**
| Error code | HTTP | Meaning |
|---|---|---|
| `not_found` | 404 | Tab or payment not found |
| `tab_not_settled` | 409 | Tab is not `complete` or `errored` |
| `not_refundable` | 409 | `booking_payment` or `pre_auth` type |
| `invalid_amount` | 409 | Amount ≤ 0 |
| `exceeds_refundable_amount` | 409 | Would exceed remaining refundable amount |
| `gift_card_refund_failed` | 409 | ROLLER gift card refund call failed |

---

#### DTO changes

**`TabDto`** — add:
```csharp
public List<RefundDto> Refunds { get; init; } = [];
```

**New `RefundDto`:**
```csharp
public class RefundDto
{
    public Guid RefundId { get; init; }
    public Guid PaymentId { get; init; }
    public decimal Amount { get; init; }
    public string Method { get; init; } = "";
    public string? Reason { get; init; }
    public string RollerRefundPushStatus { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}
```

**`PaymentDto`** — add:
```csharp
public string RefundStatus { get; init; } = "none";
public decimal AmountRefunded { get; init; }
public decimal AmountRefundable { get; init; }
```

---

### Frontend Changes

#### Receipt flyout (`tabs.html` / `tabs.ts`)

- For each payment in the receipt where `refundStatus !== 'not_refundable'`:
  - Show a **Refund** button (disabled if `refundStatus === 'refunded'`)
  - Button is disabled and shows "Not refundable — process in ROLLER" if `refundStatus === 'not_refundable'`
- Clicking Refund opens a dialog: amount input (pre-filled to remaining refundable amount), optional reason field
- On confirm: calls `POST /api/tabs/{tabId}/payments/{paymentId}/refund`; refreshes receipt and tab list

- Issued refunds shown in the receipt under the payments section:
  ```
  Visa **** 4242              -$45.00
    REFUNDED  -$45.00  [Customer request]
  ```

- If any refund has `rollerRefundPushStatus === 'failed'`, show a **Retry Sync** button per refund (Phase 7b adds meaning to this — in 7a it is shown but does nothing until 7b)

---

### ROLLER Endpoint Used

| Endpoint | Status | Notes |
|---|---|---|
| `POST /giftcards/{cardNumber}/refund` | Existing (confirm — see OQ-A) | Gift card balance restoration |

---

### Approval Gate — Phase 7a

> - Refund a card payment on a `complete` tab → `Refund` record persisted, `Payment.RefundStatus = "refunded"`, receipt flyout shows the refund line
> - Partial refund → `RefundStatus = "partial"`, refundable amount reduced correctly
> - Attempt a second refund exceeding the remaining amount → `exceeds_refundable_amount` error
> - Attempt to refund a `booking_payment` → `not_refundable` error with messaging directing to ROLLER
> - Refund a gift card payment → `POST /giftcards/{card}/refund` called, `RollerGiftCardRefundTransactionId` stored
> - Retry a failed gift card refund → idempotency key reused, succeeds

---

## Phase 7b — ROLLER Refund Sync

### Goal

Record refunds issued in the POS against the ROLLER booking, keeping ROLLER's booking state accurate. Mirrors the Phase 6 payment push pattern.

---

### What Already Exists

- `Refund.RollerRefundPushStatus` is set in Phase 7a but not yet acted on for the booking endpoint
- `RollerSyncService` provides the pattern: call ROLLER, update push status, handle retry
- `IRollerApiClient` provides `PostAsync`

---

### New ROLLER Endpoint Design

#### ⭐ `POST /bookings/{uniqueId}/refunds`

Records a refund against the booking in ROLLER.

**Request body:**
```json
{
  "id": "<refundId as 32-char hex>",
  "paymentId": "<original payment id as 32-char hex>",
  "amount": 45.00,
  "reason": "Customer request"
}
```

**Field notes:**
- `id`: `refund.RefundId.ToString("N")` — 32 chars, no hyphens (idempotency key)
- `paymentId`: the `id` field from the original payment push (i.e. `payment.PaymentId.ToString("N")`); allows ROLLER to reconcile against the payment record
- `amount`: refund amount (positive)
- `reason`: operator-entered reason or null

**Response 200:**
```json
{
  "refundId": "...",
  "status": "processed",
  "refundedAt": "ISO8601"
}
```

**Response 409 (duplicate `id`):**
```json
{ "error": "REFUND_ALREADY_PROCESSED", "refundId": "..." }
```
Treat as success (idempotent).

**Response 404:** booking not found — return error, do not change refund push status  
**Response 422:** payment not found in ROLLER (possible if payment push never succeeded) — record as `"failed"`, surface via retry

---

### Backend Changes

#### `IRollerSyncService` — add `PushRefundAsync`

```csharp
Task<string?> PushRefundAsync(string bookingUniqueId, Refund refund, CancellationToken ct = default);
```

Returns `null` on success, error code string on failure. On success: `refund.RollerRefundPushStatus = "pushed"`. On failure: `"failed"`.

---

#### `RefundService` — integrate ROLLER push

Extend `ProcessRefundAsync` after saving the refund record:

```
If tab.BookingUniqueId is not null AND payment.Type == "payment":
    var error = await rollerSync.PushRefundAsync(tab.BookingUniqueId, refund, ct);
    if error: refund.RollerRefundPushStatus = "failed" (already set in PushRefundAsync)
    else: refund.RollerRefundPushStatus = "pushed"
    await db.SaveChangesAsync(ct)
Else (no booking, or booking_payment — shouldn't reach here):
    refund.RollerRefundPushStatus = "not_applicable"
```

The overall `ProcessRefundAsync` still returns success to the caller even if the ROLLER push fails — the refund is recorded in the POS and can be retried.

**`RetrySyncAsync` flow:**
1. Load refund; validate `RollerRefundPushStatus == "failed"`
2. Load parent tab and payment
3. If gift card and `RollerGiftCardRefundTransactionId` is null: retry gift card refund first
4. If `tab.BookingUniqueId is not null`: call `PushRefundAsync`
5. Save and return `TabDto`

---

#### `TabsController` — retry endpoint

`POST /api/tabs/{tabId}/refunds/{refundId}/retry-sync` already declared in 7a. Implement using `RefundService.RetrySyncAsync`.

---

### Frontend Changes

#### Receipt flyout

- For each refund with `rollerRefundPushStatus === 'failed'`:
  - Show **Retry Sync** button (previously inactive from 7a — now wired)
  - On success: badge updates to `pushed`; no further action needed

---

### ROLLER Endpoint Used

| Endpoint | Status |
|---|---|
| `POST /bookings/{uniqueId}/refunds` | ⭐ New |
| `POST /giftcards/{cardNumber}/refund` | Existing (already called in 7a) |

---

### Approval Gate — Phase 7b

> - Refund a card payment on a booking-linked tab → `POST /bookings/{id}/refunds` called → `RollerRefundPushStatus = "pushed"`
> - Inspect the ROLLER booking — confirm the refund is recorded
> - Simulate ROLLER failure (toggle stub to 500) → `RollerRefundPushStatus = "failed"`, retry button shown
> - Use Retry Sync → succeeds, idempotency key reused, duplicate 409 treated as success
> - Refund on a non-booking tab (walk-in) → `RollerRefundPushStatus = "not_applicable"`, no ROLLER call made

---

## Phase 8 — Discounts

### Goal

Allow operators to apply discounts to an open tab. When a booking is imported from ROLLER, any existing discounts on the booking are transferred automatically. On settlement, applied discounts are synced back to ROLLER.

---

### What Already Exists

- `Tab.ImportedItemsJson` and `Tab.AddedItemsJson` — JSON column pattern reused for discounts
- `TabService` handles item add/remove and grand total recomputation — same pattern used for discounts
- `BookingService.ImportBookingAsync` already parses `items[]` and `payments[]` from booking detail — will be extended to parse `discounts[]`
- `RollerSyncService.PushItemsAsync` — will be extended to include discounts in the PUT body

---

### Database Changes

#### `Tab` — add column

```csharp
public string DiscountsJson { get; set; } = "[]";
```

JSON array of `TabDiscount` records:
```csharp
public record TabDiscount(
    string DiscountId,     // ROLLER discount ID for imported; "manual-{guid}" for POS-created
    string Name,           // display label, e.g. "10% Loyalty Discount"
    string Type,           // "percentage" | "fixed"
    decimal Value,         // the rate (10.0 for 10%) or the dollar amount
    decimal Amount,        // pre-computed dollar reduction (always positive)
    string Source          // "imported" | "manual"
);
```

#### Grand total computation change

`GrandTotal = itemsTotal − discountsTotal`

where `discountsTotal = sum(discount.Amount)`. Updated wherever `GrandTotal` is computed — `TabService.RecalculateTotal()`.

Constraint: discounts cannot reduce `GrandTotal` below $0. If a discount would push the total negative, the discount `Amount` is capped at the current pre-discount item total.

#### EF migration

`dotnet ef migrations add AddDiscounts`
- `DiscountsJson NVARCHAR(MAX)` column on `Tabs` with default `'[]'`

---

### Backend Changes

#### `BookingService.ImportBookingAsync` — parse imported discounts

Extend booking detail parsing to read `discounts[]` from the ROLLER booking response:

```csharp
var importedDiscounts = GetJsonArray(bookingDetail, "discounts")
    .Select(d => new TabDiscount(
        DiscountId: GetString(d, "discountId") ?? Guid.NewGuid().ToString(),
        Name: GetString(d, "name") ?? "Discount",
        Type: GetString(d, "type") ?? "fixed",
        Value: GetDecimal(d, "value"),
        Amount: GetDecimal(d, "amount"),
        Source: "imported"
    ))
    .ToList();
tab.DiscountsJson = JsonSerializer.Serialize(importedDiscounts);
```

If ROLLER returns discounts as negative-cost items inside `items[]` (alternate representation — see OQ-B), detect items with `cost < 0`, extract them as discounts, and exclude them from `ImportedItemsJson`.

---

#### `TabService` — new discount methods

```csharp
Task<(TabDto? tab, string? error)> AddDiscountAsync(Guid tabId, AddDiscountRequest req, CancellationToken ct);
Task<(TabDto? tab, string? error)> RemoveDiscountAsync(Guid tabId, string discountId, CancellationToken ct);
```

**`AddDiscountAsync` flow:**
1. Load tab; validate `PaymentStatus == "open"`
2. Compute dollar amount:
   - `"percentage"`: `amount = itemsTotal × value / 100` (itemsTotal before this discount)
   - `"fixed"`: `amount = value`
3. Cap: if `amount > itemsTotal − existingDiscountsTotal`, cap to that difference
4. Generate `DiscountId = "manual-" + Guid.NewGuid().ToString("N")[..8]`
5. Append to `DiscountsJson`, recompute `GrandTotal`, save

**`RemoveDiscountAsync` flow:**
1. Load tab; validate open
2. Find discount by `DiscountId`; validate `Source == "manual"` (imported discounts cannot be removed — return `cannot_remove_imported_discount`)
3. Remove from list, recompute `GrandTotal`, save

---

#### `TabsController` — new endpoints

```
POST   /api/tabs/{tabId}/discounts              — add a manual discount
DELETE /api/tabs/{tabId}/discounts/{discountId} — remove a manual discount
```

**`POST /api/tabs/{tabId}/discounts` request:**
```json
{ "name": "Staff discount", "type": "percentage", "value": 10.0 }
```

**Error responses:**
| Code | Meaning |
|---|---|
| `tab_not_open` | Tab is not in `open` state |
| `invalid_type` | `type` is not `"percentage"` or `"fixed"` |
| `invalid_value` | Value ≤ 0 |
| `would_exceed_total` | Discount would reduce total below $0 (no items to discount) |
| `cannot_remove_imported_discount` | Attempt to remove a ROLLER-imported discount |

---

#### `TabDto` / `TabSummaryDto` — changes

**`TabDto`** — add:
```csharp
public List<TabDiscount> Discounts { get; init; } = [];
public decimal DiscountTotal { get; init; }
```

**`TabSummaryDto`** — add:
```csharp
public decimal DiscountTotal { get; init; }
```
(so the tabs list shows the discounted amount owing correctly)

---

#### `RollerSyncService.PushItemsAsync` — include discounts

Extend the `PUT /bookings/{uniqueId}` request body to carry the discount list (pending OQ-C confirmation):

```csharp
var body = new
{
    newItems = newItems,
    discounts = addedDiscounts.Select(d => new
    {
        name = d.Name,
        type = d.Type,           // "percentage" | "fixed"
        value = d.Value,
        amount = d.Amount
    }).ToList()
};
```

Only `"manual"` discounts are sent in the sync — imported discounts were already on the booking.

If OQ-C confirms ROLLER does not support `discounts` in the PUT body, omit this field and add a `// TODO: production gap — manual discounts not synced to ROLLER` comment.

---

### Frontend Changes

#### Tab panel (`tab.html` / `tab.ts`)

- **Discounts section** below the item list, above the grand total:
  - Imported discounts shown in grey with a lock icon (not removable)
  - Manual discounts shown with a remove (×) button
  - Grand total line updates reactively

- **Add Discount** button (only visible when tab is open):
  - Opens a dialog: Name field, Type toggle (`%` / `$`), Value input
  - "Apply" button: calls `POST /api/tabs/{tabId}/discounts`
  - Validates locally: value > 0

- Display format:
  ```
  Discounts
  ─────────────────────────────────
  10% Loyalty Discount (imported)   −$15.00
  Staff discount (10%)              −$4.50   [×]
  ─────────────────────────────────
  Total after discounts             $40.50
  ```

#### Receipt flyout (`tabs.html`)

- Add discount lines between items and totals:
  ```
  10% Loyalty Discount          −$15.00
  Staff discount                 −$4.50
  ─────────────────────────────────────
  TOTAL (after discounts)        $40.50
  ```

---

### ROLLER Endpoints Used

| Endpoint | Status | Notes |
|---|---|---|
| `GET /bookings/{uniqueId}` | Existing — extended usage | Discounts parsed from response |
| `PUT /bookings/{uniqueId}` | Existing — extended | `discounts` field added to body (OQ-C) |

---

### Approval Gate — Phase 8

> - Import a ROLLER booking that has discounts → discounts appear on the tab, grand total is reduced, amount owing is correct
> - Add a manual percentage discount to an open tab → total updates immediately
> - Add a manual fixed discount → total updates immediately
> - Attempt to remove an imported discount → `cannot_remove_imported_discount` error
> - Remove a manual discount → total restored
> - Settle a discounted tab → `PUT /bookings/{id}` carries `discounts` in the body (or gap documented if OQ-C unresolved)
> - Receipt shows discount lines and correctly reduced total

---

## Phase 9 — Product Modifiers

### Goal

Surface ROLLER product modifiers in the POS catalogue so operators can select them when adding a product to the tab. Selected modifiers affect the line item price and are included when the item is pushed back to ROLLER on settlement.

---

### What Already Exists

- `ProductService` fetches and caches the product catalogue — extended to carry modifier groups
- `TabLineItem` record stored in `AddedItemsJson` — extended with optional modifiers
- `TabService.AddItemAsync` handles item addition — extended to accept modifier selections
- `RollerSyncService.PushItemsAsync` builds `newItems` — extended to carry modifier IDs

---

### Database Changes

No new tables. Changes are JSON-only:

#### `TabLineItem` — extend with optional modifiers

```csharp
public record TabLineItem(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,             // effective price (base + sum of modifier adjustments)
    decimal BaseUnitPrice,         // original product price before modifiers (0 if no modifiers)
    List<TabLineItemModifier>? Modifiers
);

public record TabLineItemModifier(
    string ModifierId,
    string ModifierName,
    decimal PriceAdjustment        // signed delta per unit (+/-)
);
```

`BaseUnitPrice` defaults to `UnitPrice` for items with no modifiers (backwards-compatible with existing JSON — `BaseUnitPrice` deserialises as 0 if absent; treat 0 as "use UnitPrice").

`GrandTotal` computation is unaffected — it already uses `UnitPrice × Quantity`, which now includes modifier adjustments.

---

### Backend Changes

#### `ProductService` — expose modifier groups

Extend the `RollerProduct` private model:
```csharp
public List<RollerModifierGroup>? ModifierGroups { get; set; }

private sealed class RollerModifierGroup
{
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    public bool Required { get; set; }
    public bool MultiSelect { get; set; }
    public List<RollerModifier>? Modifiers { get; set; }
}

private sealed class RollerModifier
{
    public string? ModifierId { get; set; }
    public string? Name { get; set; }
    public decimal PriceAdjustment { get; set; }
}
```

Map to new `ModifierGroupDto` in `ProductDto`:
```csharp
public record ProductDto(
    string ProductId,
    string Name,
    string ParentName,
    decimal Price,
    string ProductType,
    string ProductSubType,
    string? Category,
    string? ImageUrl,
    List<ModifierGroupDto> ModifierGroups    // empty list if no modifiers
);

public record ModifierGroupDto(
    string GroupId,
    string GroupName,
    bool Required,
    bool MultiSelect,
    List<ModifierDto> Modifiers
);

public record ModifierDto(
    string ModifierId,
    string Name,
    decimal PriceAdjustment
);
```

Cache unchanged — modifier groups are included alongside product data and cached for the same 30-minute TTL.

If OQ-D confirms modifiers come from a separate endpoint, add a `GET /products/{id}/modifiers` call inside `EnsureCacheAsync` per product (or a single batch call if ROLLER supports it), cached together.

---

#### `AddItemRequest` — extend with modifiers

```csharp
public record AddItemRequest(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    List<SelectedModifier>? Modifiers
);

public record SelectedModifier(
    string ModifierId,
    string ModifierName,
    decimal PriceAdjustment
);
```

---

#### `TabService.AddItemAsync` — modifier validation and effective price

1. If the product has required modifier groups (from product catalogue), validate that each required group has at least one selection in `req.Modifiers`. Return `missing_required_modifier` with the group name if not.
2. Compute effective unit price: `effectiveUnitPrice = req.UnitPrice + req.Modifiers.Sum(m => m.PriceAdjustment)`
3. Store as `TabLineItem` with `BaseUnitPrice = req.UnitPrice`, `UnitPrice = effectiveUnitPrice`, `Modifiers = [...]`
4. Merging logic change: items are only merged into an existing line item if they share the same `ProductId` **and** the same set of selected modifier IDs. Items with different modifier selections become separate line items.

---

#### `RollerSyncService.PushItemsAsync` — include modifier IDs

Extend `newItems` to carry selected modifiers (pending OQ-E):

```csharp
var newItems = addedItems.Select(i => new
{
    productId = int.Parse(i.ProductId),
    quantity = i.Quantity,
    bookingDate = today,
    modifiers = i.Modifiers?.Select(m => new { modifierId = int.Parse(m.ModifierId) }).ToList(),
    priceOverride = (decimal?)null
}).ToList();
```

**Fallback if OQ-E confirms ROLLER does not accept `modifiers` in `newItems`:**

Pass `priceOverride` instead so the booking total is at least correct:
```csharp
priceOverride = i.Modifiers?.Any() == true ? (decimal?)i.UnitPrice : null
```
Document as a gap: modifier names will not be visible in ROLLER, only the adjusted price.

---

### Frontend Changes

#### Catalogue (`catalogue.html` / `catalogue.ts`)

**"Add to Tab" interaction — with modifiers:**

1. When the user clicks "Add to Tab" for a product that has `modifierGroups.length > 0`:
   - Open a **Modifier Selection Dialog** (`MatDialog`) instead of adding immediately
   - Dialog shows each modifier group as a section with `mat-radio-group` (single-select) or `mat-checkbox` list (multi-select)
   - Required groups show a red asterisk; their section is highlighted if nothing is selected
   - "Add to Tab" button in the dialog is disabled until all required groups have a selection
   - Optional modifier groups can be left unselected

2. Dialog footer shows a live price calculation:
   ```
   Base price:        $5.00
   + Extra Shot:     +$0.50
   ─────────────────────────
   Effective price:   $5.50  × 2 = $11.00
   ```

3. On confirm: calls `POST /api/tabs/{tabId}/items` with modifiers array; dismisses dialog

4. Products with no modifier groups: existing "Add to Tab" inline behaviour unchanged (no dialog)

---

#### Tab panel (`tab.html` / `tab.ts`)

- Modifier selections shown as a sub-line under each line item:
  ```
  Coffee × 2                          $11.00
    + Extra Shot  (+$0.50/ea)
  ```
- Quantity controls and remove button operate on the full line item (including its modifiers)

---

#### Receipt flyout (`tabs.html`)

- Receipt shows modifier detail indented under each line item:
  ```
  Coffee             2   $5.50   $11.00
    + Extra Shot (+$0.50)
  ```

---

### ROLLER Endpoints Used

| Endpoint | Status | Notes |
|---|---|---|
| `GET /data/products` | Existing — extended | `modifierGroups` parsed from response |
| `PUT /bookings/{uniqueId}` | Existing — extended | `modifiers` per item in `newItems` (OQ-E) |

---

### Approval Gate — Phase 9

> - Load the catalogue — products with modifiers show a "Customise" indicator
> - Add a product with a required modifier without selecting → Add button remains disabled
> - Select modifiers → effective price shown, item added to tab with correct total
> - Two of the same product with different modifier selections → two separate line items on the tab
> - Settle a tab with modified items → `PUT /bookings/{id}` carries `modifiers` per item (or `priceOverride` fallback if OQ-E unresolved)
> - Inspect ROLLER booking — modifier selections visible (or price override applied if fallback)
> - Product with no modifiers → no dialog, existing inline add behaviour unchanged

---

## Effort Estimates

| Phase | Description | Estimate |
|---|---|---|
| 7a | POS-side refunds (gift card + simulated card/cash) | 2–3 days |
| 7b | New ROLLER refund endpoint + sync | 1–2 days |
| 8 | Discounts (import + manual + sync) | 2–3 days |
| 9 | Modifiers (catalogue + tab + sync) | 3–4 days |
| **Total** | | **8–12 days** |

> Phase 7a can begin immediately. Phase 7b requires ROLLER-side endpoint build — POS stub enables parallel development. Phase 8 is blocked on OQ-B and OQ-C confirmation. Phase 9 is blocked on OQ-D and OQ-E confirmation.

---

## Critical Path

```
Phase 7a ──► Phase 7b (ROLLER-side endpoint built in parallel)
Phase 8  (unblocked after OQ-B/C confirmed)
Phase 9  (unblocked after OQ-D/E confirmed — can run parallel to Phase 8)
```

Phases 8 and 9 are independent of each other and can be built in parallel by separate developers once their open questions are resolved.
