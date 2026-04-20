# ROLLER F&B POS — Phased Implementation Plan

**Prototype goal:** Demonstrate end-to-end ROLLER integration for a whitelabelled, browser-based F&B point-of-sale.  
**Stack:** Angular (latest stable) · .NET 9 Web API (C#) · SQL Server + EF Core · ngrok (webhooks)  
**Date produced:** 2026-04-20  

---

## Open Questions — Stated Assumptions

Before the phase breakdown, the three unresolved open questions are answered with explicit assumptions that constrain later design choices.

### OQ5 — Webhook conflict: booking amended while F&B items already on open tab

**Assumption:** The POS will **never silently alter an open tab** in response to a webhook. When a `booking_updated` event arrives and the refreshed booking state conflicts with items already on the tab (e.g. a line item was removed in ROLLER that already appears on the tab), the system will:

1. Re-fetch the booking and diff it against the current tab snapshot.
2. Surface a **reconciliation warning banner** in the operator UI — listing each conflicting line item by name and the nature of the conflict (removed / quantity changed / price changed).
3. Leave the tab unchanged. The operator manually decides whether to remove the item (and take the adjustment) or keep it and absorb the discrepancy as a manual override.
4. Record the conflict and the operator's eventual resolution in the tab's audit log (a JSON column on the Tab record).

This keeps the prototype safe without requiring automated rollback logic whose business rules are not yet defined.

---

### OQ8 — Can a ROLLER operator manually release a stuck payment lock from within ROLLER?

**Assumption:** No such release mechanism exists in ROLLER today (the lock endpoint is being built fresh). The prototype will **not add one to the ROLLER side** — a ROLLER-side UI override is out of scope. Instead, the POS exposes an **admin REST endpoint** (`DELETE /api/admin/tabs/{tabId}/lock`) protected by a hardcoded admin token (config-driven), which calls the ROLLER unlock endpoint. This gives the venue manager or ROLLER support team a direct escape hatch without needing a ROLLER-side UI. The assumption is that this is acceptable for a prototype but a ROLLER-side override should be scoped for production.

---

## Project Structure Overview

```
prototype-hospitality-pos/
├── docs/
│   ├── api-contracts/          # Phase 0 output
│   │   ├── payment-lock.md
│   │   ├── push-charges.md
│   │   └── gift-card.md
│   └── IMPLEMENTATION_PLAN.md  # this file
├── pos-backend/                # .NET 9 Web API
│   ├── PosApi/
│   │   ├── Controllers/
│   │   ├── Services/
│   │   │   └── Roller/         # thin ROLLER API client
│   │   ├── Models/
│   │   ├── Data/               # EF Core DbContext + migrations
│   │   └── Hubs/               # SignalR for real-time webhook push to UI
│   └── PosApi.Tests/
└── pos-frontend/               # Angular app
    ├── src/
    │   ├── app/
    │   │   ├── core/           # services, interceptors, ROLLER client wrapper
    │   │   ├── features/
    │   │   │   ├── catalogue/
    │   │   │   ├── tab/
    │   │   │   ├── booking-search/
    │   │   │   ├── payment/
    │   │   │   └── receipt/
    │   │   └── shared/         # components, pipes
    │   └── environments/
    └── angular.json
```

---

## Phase 0 — API Discovery and Contract Design

**Goal:** Produce approved API contracts for all five new ROLLER endpoints and confirm field names / enum values for all existing endpoints before any dependent code is written. No application code is produced in this phase.

### Tasks

#### T0.1 — Review existing ROLLER endpoints (live docs)

For each of the five existing endpoints below, load the live documentation page, record the exact request schema, response schema, HTTP method, path, authentication header format, and any enum values relevant to this project.

| Endpoint | Docs URL | Key fields to confirm |
|----------|----------|-----------------------|
| Get Products | https://docs.roller.app/docs/roller-api/7bbac8eaac480-get-products | `productType`, `productSubType` enum values for F&B; pagination shape; price field name and currency format |
| Search Bookings | https://docs.roller.app/docs/rest-api/fbb465d1ed24d-search-for-bookings | Query params (name / email / bookingId); response shape; pagination |
| Get Booking Detail | https://docs.roller.app/docs/rest-api/olt8a8nxs75ev-get-detail-of-a-bookingv | Full booking schema: line items, tickets, totals, status enum values |
| Update Booking | https://docs.roller.app/docs/rest-api/v4mzj4t4erwa9-update-a-booking | Which fields are writable; field names for guest count, time slot, capacity |
| Add Transaction Record | https://docs.roller.app/docs/rest-api/a86n5aasxe98r-add-transaction-record | Required vs optional fields; whether it supports line-level F&B detail or is a single total; idempotency key support |

**Output for T0.1:** A single Markdown table (`docs/api-contracts/existing-endpoints.md`) containing confirmed field names, enum values, and any gaps or surprises found.

#### T0.2 — Confirm F&B product filter enum values

From the live `/products` response against the real ROLLER environment, identify the exact string values for `productType` and `productSubType` that correspond to F&B items. Record these as constants in the contract document; they will be hardcoded in the backend filter. Do not guess — call the endpoint with the provided API key and inspect the raw response.

#### T0.3 — Confirm webhook payload and signature mechanism

Review ROLLER's webhook documentation to determine:
- The exact event name for booking amendments (assumed `booking_updated` but confirm).
- Whether payload origin is validated via HMAC-SHA256 signature header, shared secret, or IP allowlist.
- The name and format of the signature header.
- Whether the webhook payload contains the full booking object or only a delta / booking ID.

**Output for T0.3:** A short section in `docs/api-contracts/webhook.md` documenting the confirmed signature mechanism and payload shape.

#### T0.4 — Design new ROLLER endpoint: Payment Lock / Unlock

Design the request/response contract for two new ROLLER endpoints. These will be implemented by the ROLLER team (or stubbed in Phase 3 if ROLLER implementation lags).

**Lock endpoint**

```
POST /api/v1/bookings/{bookingId}/payment-lock

Request body:
{
  "lockedBySystem": "pos",          // string identifier of the locking system
  "lockedByReference": "{tabId}",   // POS tab ID for traceability
  "reason": "tab_opened"            // enum: tab_opened
}

Response 200:
{
  "lockId": "uuid",
  "bookingId": "string",
  "lockedAt": "ISO8601",
  "lockedBySystem": "pos",
  "lockedByReference": "string",
  "status": "locked"
}

Response 409 (already locked by another system):
{
  "error": "booking_already_locked",
  "lockedBySystem": "string",
  "lockedAt": "ISO8601"
}
```

**Unlock endpoint**

```
DELETE /api/v1/bookings/{bookingId}/payment-lock

Request body:
{
  "lockId": "uuid",                 // must match the lockId returned at lock time
  "reason": "tab_settled"           // enum: tab_settled | manual_override | system_crash_recovery
}

Response 200:
{
  "bookingId": "string",
  "unlockedAt": "ISO8601",
  "status": "unlocked"
}

Response 404: lock not found
Response 403: lockId does not match current lock
```

**Constraints to document:**
- Non-payment fields (time slot, guest count, capacity) must remain editable while a lock is active. The ROLLER-side implementation must enforce this at the field level.
- Refund path must remain accessible regardless of lock state.
- No server-side TTL in the prototype (per OQ6 assumption). Flag this as a production gap.

**Output for T0.4:** `docs/api-contracts/payment-lock.md`

#### T0.5 — Design new ROLLER endpoint: Push F&B Charges

```
POST /api/v1/bookings/{bookingId}/fnb-charges

Request body:
{
  "tabId": "string",               // POS tab reference
  "idempotencyKey": "uuid",        // caller-generated; safe to retry
  "lineItems": [
    {
      "productId": "string",
      "productName": "string",
      "quantity": 2,
      "unitPriceExclGst": 1000,    // integer cents
      "unitPriceInclGst": 1100,
      "gstAmount": 100,
      "lineTotal": 2200
    }
  ],
  "paymentMethod": "card" | "gift_card",
  "paymentReference": "string",    // card number (masked) or gift card number
  "totalAmountInclGst": 2200,
  "currency": "AUD",
  "settledAt": "ISO8601"
}

Response 200:
{
  "chargeId": "uuid",
  "bookingId": "string",
  "status": "posted",
  "postedAt": "ISO8601"
}

Response 409 (duplicate idempotencyKey):
{
  "error": "duplicate_request",
  "chargeId": "uuid"               // return the original chargeId
}
```

**Output for T0.5:** `docs/api-contracts/push-charges.md`

#### T0.6 — Design new ROLLER endpoints: Gift Card Operations

Three endpoints. ROLLER may already have partial gift card support — confirm against docs before designing from scratch.

**Balance lookup**
```
GET /api/v1/gift-cards/{cardNumber}/balance

Response 200:
{
  "cardNumber": "string",
  "balanceCents": 5000,
  "currency": "AUD",
  "status": "active" | "depleted" | "expired" | "cancelled"
}
```

**Deduction (redemption)**
```
POST /api/v1/gift-cards/{cardNumber}/deduct

Request body:
{
  "amountCents": 2200,
  "currency": "AUD",
  "idempotencyKey": "uuid",
  "reference": "{tabId}"
}

Response 200:
{
  "transactionId": "uuid",
  "cardNumber": "string",
  "amountDeducted": 2200,
  "remainingBalance": 2800,
  "status": "success"
}

Response 422:
{
  "error": "insufficient_balance",
  "availableBalance": 1500
}
```

**Refund**
```
POST /api/v1/gift-cards/{cardNumber}/refund

Request body:
{
  "originalTransactionId": "uuid",
  "amountCents": 2200,
  "idempotencyKey": "uuid"
}

Response 200:
{
  "refundTransactionId": "uuid",
  "cardNumber": "string",
  "amountRefunded": 2200,
  "newBalance": 4800
}
```

**Output for T0.6:** Gift card section in `docs/api-contracts/gift-card.md`

#### T0.7 — Collate and circulate contract document for approval

Consolidate outputs from T0.1–T0.6 into a single `docs/api-contracts/CONTRACTS.md` index with links to each contract file. Circulate to the ROLLER team and stakeholders for sign-off.

### ROLLER Endpoints Used in Phase 0

| Endpoint | Status |
|----------|--------|
| GET /products | Real — called to confirm enum values (T0.2) |
| All others | Documentation review only — no code |

### Dependencies
- ROLLER API key (provided, hardcoded in `.env` / `appsettings.Development.json`)
- Access to live ROLLER environment with real product data for T0.2

### Effort Estimate
**2–3 days** (1 day for T0.1–T0.3 live doc review; 1–2 days for T0.4–T0.6 contract drafting and revision cycles with ROLLER team)

### Approval Gate — Phase 0
> **Before Phase 1 begins:** The `CONTRACTS.md` document must be reviewed and approved by the ROLLER team (or a nominated technical stakeholder). Specific sign-off items:
> - Confirmed F&B `productType` / `productSubType` enum values (used in backend filter)
> - Confirmed webhook signature mechanism
> - Payment lock contract approved (this unblocks Phase 3 ROLLER-side build)
> - Push charges contract approved (this unblocks Phase 6)
> - Gift card contracts approved (this unblocks Phase 5 gift card path)

---

## Phase 1 — Working POS with Product Catalogue and Tabs

**Goal:** A working browser-based POS — operator can browse the F&B product catalogue and create/manage a tab with items and a running total. No booking integration yet.

### Phase 1a — Project Scaffold

#### T1a.1 — Backend scaffold

```
dotnet new webapi -n PosApi --framework net9.0
```

- Add NuGet packages: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Tools`, `Swashbuckle.AspNetCore` (Swagger), `Microsoft.AspNetCore.SignalR` (for Phase 4 real-time push).
- Create `appsettings.json` with:
  ```json
  {
    "ConnectionStrings": {
      "Pos": "Server=localhost;Database=RollerPos;Trusted_Connection=True;"
    },
    "Roller": {
      "BaseUrl": "https://api.roller.app",
      "ApiKey": ""   // populated from environment variable / user secrets
    },
    "AdminToken": ""  // for the force-release endpoint (OQ8)
  }
  ```
- Load `ROLLER__ApiKey` from environment / `.env` — never commit the key.
- Enable CORS for `http://localhost:4200` (Angular dev server).
- Add `GlobalExceptionMiddleware` that catches unhandled exceptions and returns `{ "error": "...", "detail": "..." }` JSON — prevents blank 500s from reaching the UI.

#### T1a.2 — EF Core data models and initial migration

**Tab entity:**
```csharp
public class Tab
{
    public Guid TabId { get; set; }
    public string? BookingId { get; set; }          // null until Phase 2
    public string ImportedItemsJson { get; set; } = "[]";  // JSON column
    public string AddedItemsJson { get; set; } = "[]";     // JSON column
    public string AuditLogJson { get; set; } = "[]";       // conflict log (OQ5)
    public decimal GrandTotal { get; set; }
    public string? PreAuthCardNumber { get; set; }
    public string PreAuthStatus { get; set; } = "none";
    public string PaymentStatus { get; set; } = "open";    // open|settled|failed|walkout_pending
    public DateTime OpenedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
```

**Payment entity:**
```csharp
public class Payment
{
    public Guid PaymentId { get; set; }
    public Guid TabId { get; set; }
    public Tab Tab { get; set; } = null!;
    public string Type { get; set; } = "";     // pre_auth|settlement|walkout_charge|gift_card
    public string Method { get; set; } = "";   // card|gift_card
    public string? CardNumber { get; set; }    // 16-digit simulated or masked gift card
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Status { get; set; } = "";   // pending|success|failed
    public string RollerPushStatus { get; set; } = "not_pushed"; // not_pushed|pushed|failed
    public DateTime CreatedAt { get; set; }
}
```

Run `dotnet ef migrations add InitialCreate` and `dotnet ef database update`.

#### T1a.3 — ROLLER API client (thin module)

Create `Services/Roller/RollerApiClient.cs`:

```csharp
public interface IRollerApiClient
{
    Task<T> GetAsync<T>(string path, CancellationToken ct = default);
    Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default);
    Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default);
    Task DeleteAsync(string path, object? body = null, CancellationToken ct = default);
}
```

- Backed by `HttpClient` registered via `IHttpClientFactory`.
- All calls set `Authorization: Bearer {apiKey}` and `Content-Type: application/json`.
- All HTTP errors deserialize the ROLLER error body and throw a typed `RollerApiException` — never let raw HTTP exceptions leak to controllers.
- No business logic in this class. It is a pure transport wrapper.

Register in `Program.cs`:
```csharp
builder.Services.AddHttpClient<IRollerApiClient, RollerApiClient>(client => {
    client.BaseAddress = new Uri(config["Roller:BaseUrl"]!);
});
```

#### T1a.4 — Angular scaffold

```
ng new pos-frontend --routing --style=scss --ssr=false
ng add @angular/material
```

- Configure `environment.ts` with `apiBaseUrl: 'http://localhost:5000'`.
- Add `HttpClientModule` and a top-level `ApiService` that wraps `HttpClient` with a base URL interceptor.
- Add an `ErrorInterceptor` that catches HTTP errors, extracts the `error` field from JSON responses, and dispatches to a shared `NotificationService` (toast/snackbar).
- Create shell layout: sidebar nav + main content area using Angular Material `mat-sidenav`.

#### T1a.5 — Stub health-check endpoint

`GET /api/health` returns `{ "status": "ok", "rollerConnected": true/false }` — the backend pings ROLLER's base URL to verify connectivity. Used as a smoke test at the end of Phase 1a.

**ROLLER Endpoints Used:** None (health check only pings base URL, no authenticated call yet)

**Effort:** 1–2 days

---

### Phase 1b — Product Catalogue

#### T1b.1 — Backend: fetch and filter products

Create `Services/Roller/ProductService.cs`:

```csharp
public interface IProductService
{
    Task<IEnumerable<FnbProduct>> GetFnbProductsAsync(CancellationToken ct = default);
}
```

- Call `GET /products` (confirmed path from T0.1) via `IRollerApiClient`.
- Filter response to items where `productType` is in the confirmed F&B type enum (from T0.2). Apply `productSubType` secondary filter if relevant.
- Map to `FnbProduct` DTO: `{ productId, name, description, priceInclGst, priceExclGst, gstAmount, category, imageUrl? }`.
- Cache the result in memory (`IMemoryCache`) for 5 minutes — the catalogue does not change during a session.

`GET /api/products/fnb` — returns the filtered, cached list. No pagination needed for the prototype (assumption: F&B catalogue is small enough to return in one call; if ROLLER paginates, fetch all pages server-side before caching).

#### T1b.2 — Frontend: catalogue view

Route: `/catalogue`

- Grid of `mat-card` components, one per product.
- Each card shows: name, price (incl. GST), category badge.
- Category filter tabs at the top (derived from distinct `category` values in the response).
- "Add to tab" button on each card — disabled if no tab is open.
- Loading skeleton while fetching; error state with retry button.

**ROLLER Endpoints Used:**

| Endpoint | Status |
|----------|--------|
| GET /products | Real |

**Effort:** 1–2 days

---

### Phase 1c — Tab Management

#### T1c.1 — Backend: tab CRUD

```
POST   /api/tabs              — create a new empty tab
GET    /api/tabs/{tabId}      — get tab detail (with payments)
POST   /api/tabs/{tabId}/items — add an item to the tab
DELETE /api/tabs/{tabId}/items/{productId} — remove an item (decrement qty; remove at 0)
```

`POST /api/tabs` body: none (or optional `bookingId` for Phase 2). Returns the new `Tab` record.

`POST /api/tabs/{tabId}/items` body:
```json
{ "productId": "string", "productName": "string", "quantity": 1, "unitPriceInclGst": 1100, "unitPriceExclGst": 1000, "gstAmount": 100 }
```

The `addedItemsJson` column holds a JSON array of these line items, grouped by `productId` (merge on add, remove on delete). Recompute `grandTotal` on every write.

#### T1c.2 — Frontend: open tab panel

Persistent right-hand drawer (collapsed by default, opens when a tab is active):

- **Open Tab** button in the top bar — calls `POST /api/tabs`, stores `tabId` in a `TabStateService` (Angular service with `BehaviorSubject`).
- Tab drawer shows: line items table (name, qty, unit price, line total), running grand total, "Remove" button per item.
- "+ / −" quantity controls on each catalogue card update the tab in real time via the `PATCH /items` endpoints.
- "Close Tab" button (destructive) — only available when tab has no items and no payment; calls a `DELETE /api/tabs/{tabId}` endpoint.

**ROLLER Endpoints Used:** None (all local persistence)

**Effort:** 1–2 days

---

### Phase 1 Approval Gate

> **Demonstrate before Phase 2 begins:**
> - Browser at `http://localhost:4200` shows a product catalogue loaded from the real ROLLER API, filtered to F&B items.
> - Operator can add items to a tab; running total updates correctly.
> - Tab state persists to SQL Server (verify via SSMS or `ef dbcontext` query).
> - Removing an item updates the total.
> - Error states are visible (pull network cable, reload — error toast appears, catalogue shows retry button).
> - `GET /api/health` returns `rollerConnected: true`.

---

## Phase 2 — Booking Search, Import, and Pre-Auth Simulation

**Goal:** Search for a real ROLLER booking, import it as a tab, and simulate a pre-authorised card.

### Tasks

#### T2.1 — Backend: booking search

`GET /api/bookings/search?q={query}` — the backend calls ROLLER's Search Bookings endpoint with the query string and returns a mapped list of `BookingSummary` DTOs:

```json
[
  {
    "bookingId": "string",
    "guestName": "string",
    "guestEmail": "string",
    "bookingDate": "ISO8601",
    "status": "string",
    "totalAmountInclGst": 0,
    "lineItemCount": 0
  }
]
```

The ROLLER search endpoint is called with the query routed to whichever search fields ROLLER supports (confirmed in T0.1 — name, email, or bookingId). If ROLLER returns multiple results for a bookingId lookup, return all and let the operator choose.

Edge cases handled server-side:
- Empty result → return `[]` (not 404).
- ROLLER API error → return `500` with structured error body.
- Query shorter than 3 characters → return `400` (prevent accidental full-catalogue dumps).

#### T2.2 — Backend: booking import and tab creation

`POST /api/tabs/from-booking` body: `{ "bookingId": "string" }`

Steps:
1. Call ROLLER Get Booking Detail endpoint. Validate the response is a booking we can work with (not cancelled, not already fully paid).
2. Map ROLLER line items to the `importedItemsJson` format (same structure as `addedItemsJson` — products already on the booking).
3. Compute `grandTotal` from the imported items.
4. Generate a random 16-digit card number for the pre-auth (format: `XXXX-XXXX-XXXX-XXXX`, digits only, random via `Random.Shared.Next`). Store as `preAuthCardNumber`.
5. Set `preAuthStatus = "simulated"`, `paymentStatus = "open"`.
6. Persist the new `Tab` record.
7. Persist a `Payment` record: `type = "pre_auth"`, `method = "card"`, `cardNumber = preAuthCardNumber`, `amount = grandTotal`, `status = "success"`.
8. Return the full tab with the pre-auth card number.

Fully pre-paid booking guard: if the booking's `remainingBalance` (or equivalent field — confirm in T0.1) is zero, return `409` with `{ "error": "booking_fully_prepaid" }`. The UI will show a "No tab needed" message.

#### T2.3 — Frontend: booking search UI

Route: `/booking-search`

- Search bar with a debounced input (300 ms) — calls `GET /api/bookings/search?q=`.
- Results list: guest name, booking date, booking ID, status badge.
- "Import" button per result — calls `POST /api/tabs/from-booking`.
- On success: navigate to the tab view, show a **Pre-Auth Card** modal/card displaying the generated card number and a success message. The card number must be clearly visible and labelled "Simulated Pre-Authorisation".
- Error states:
  - No results → "No bookings found. Try a different search term."
  - Multiple matches for a booking ID → show all results; operator selects one.
  - Fully pre-paid → "This booking has been fully paid. No tab required."
  - ROLLER API error → toast with error detail.

#### T2.4 — Prevent duplicate tab creation

Before creating a tab from a booking, check whether an open tab already exists for that `bookingId`. If so, return `409` with `{ "error": "tab_already_open", "existingTabId": "uuid" }`. The UI navigates to the existing tab instead of creating a new one.

**ROLLER Endpoints Used:**

| Endpoint | Status |
|----------|--------|
| POST /search-bookings (or equivalent) | Real |
| GET /bookings/{bookingId} | Real |

**Effort:** 2–3 days

### Phase 2 Approval Gate

> **Demonstrate before Phase 3 begins:**
> - Search for a real ROLLER booking by name, email, and booking ID — all three work.
> - Import a booking — tab is created in SQL Server with correct imported items.
> - Pre-auth card number is displayed in the UI immediately after import.
> - A `Payment` record with `type = "pre_auth"` and `status = "success"` exists in the database.
> - Attempting to import the same booking twice shows the "tab already open" message.
> - A fully pre-paid booking shows "No tab needed" and does not create a tab.

---

## Phase 3 — Payment Lock Mechanism

**Goal:** Design and build the ROLLER-side payment lock/unlock endpoint, then wire it into the POS booking import flow.

### Tasks

#### T3.1 — POS backend: integrate payment lock

Modify the `POST /api/tabs/from-booking` flow (T2.2) to:

1. Attempt booking import and create the `Tab` record in a pending state.
2. Call `POST /api/v1/bookings/{bookingId}/payment-lock` 
4. If the lock call fails:
   - Delete the pending tab record.
   - Return `503` to the UI with `{ "error": "payment_lock_failed", "detail": "..." }`.
   - The UI shows: "Could not lock this booking. Another system may have it open. Please try again or contact support."
5. If the lock call succeeds, complete the tab creation and pre-auth simulation (T2.2 steps 4–8).

This makes lock acquisition atomic with tab creation from the UI's perspective.

#### T3.2 — POS backend: admin force-release endpoint (OQ8)

`DELETE /api/admin/tabs/{tabId}/lock`

- Requires `Authorization: Bearer {AdminToken}` header (token from `appsettings`).
- Calls the ROLLER unlock endpoint for all tabs in the system


**ROLLER Endpoints Used:**

| Endpoint | Status |
|----------|--------|
| POST /bookings/{id}/payment-lock | New |
| DELETE /bookings/{id}/payment-lock | New  |


### Phase 3 Approval Gate

> **Demonstrate before Phase 4 begins:**
> - Import a booking → lock endpoint is called → `lockId` stored on the tab.
> - Inspect the ROLLER booking (or stub state) and confirm it shows `status: "locked"`.
> - Edit a non-payment field on the locked booking in ROLLER (e.g. guest count) → succeeds.
> - Attempt to import the same booking from a second browser tab → receives "payment lock failed" error in the UI.
> - Confirm the stuck lock monitor runs and flags a manually-aged tab.
> - Admin force-release endpoint successfully unlocks a stuck tab via the stub.

---

## Phase 4 — Webhook Handling

**Goal:** Receive and process real `booking_updated` events from ROLLER in real time, surfacing changes to the operator without polling.

### Tasks

#### T4.1 — Backend: webhook endpoint

`POST /api/webhooks/roller`

- Accept ROLLER's webhook `POST` payload (Content-Type: `application/json`).
- Validate payload origin using the mechanism confirmed in T0.3:
  - If HMAC-SHA256: compute `HMAC-SHA256(rawBody, sharedSecret)` and compare to the signature header (constant-time comparison to prevent timing attacks). Return `401` if invalid.
  - If shared secret header: compare header value to config secret. Return `401` if invalid.
- Return `200` immediately after validation — do not make the ROLLER webhook call wait for processing.
- Enqueue the validated payload to an in-process `Channel<WebhookPayload>` for background processing.

#### T4.2 — Backend: webhook processor

`Services/WebhookProcessorService.cs` — a `BackgroundService` that reads from the `Channel`:

1. Confirm event type is `booking_updated` (or the confirmed event name from T0.3). Ignore unknown event types (return without error).
2. Extract `bookingId` from the payload.
3. Check whether an open tab exists for this `bookingId`. If not, log and return — no action needed.
4. Call ROLLER Get Booking Detail to fetch the current booking state.
5. Diff the refreshed booking state against `tab.importedItemsJson`:
   - Items removed in ROLLER but present on tab → conflict items list.
   - Price changes on items present on tab → conflict items list.
   - New items added in ROLLER → informational update (add to `importedItemsJson`, recompute total).
   - Non-item changes (time slot, guest count) → informational update only.
6. Merge non-conflicting changes into the tab record in the database.
7. If conflict items exist (OQ5 assumption): append a conflict entry to `tab.AuditLogJson`, set a `HasPendingConflict` boolean column on the tab.
8. Use SignalR to push a `tab-updated` event to all clients subscribed to that `tabId`:
   ```json
   {
     "tabId": "uuid",
     "eventType": "booking_updated" | "conflict_detected",
     "conflictItems": [...],   // empty array if no conflicts
     "updatedFields": [...]
   }
   ```

#### T4.3 — Backend: SignalR hub

`Hubs/TabHub.cs`:
- Clients join a group named `tab-{tabId}` on connection.
- The webhook processor calls `_hubContext.Clients.Group(...)` to send events.

#### T4.4 — ngrok setup and ROLLER webhook registration

- Install ngrok; add a `start-ngrok.sh` helper script.
- Run `ngrok http 5000` and copy the HTTPS forwarding URL.
- Register the webhook URL `https://{ngrok-id}.ngrok-free.app/api/webhooks/roller` with ROLLER (confirm registration mechanism from T0.3 — API call or portal UI).
- Document the registration step in `docs/DEVELOPMENT_SETUP.md` (ngrok URL changes each restart; this is expected for local development).

#### T4.5 — Frontend: real-time conflict notification

- Add `@microsoft/signalr` to Angular project: `npm install @microsoft/signalr`.
- Create `SignalRService` — connects to `/hubs/tab`, joins the current tab's group.
- On `tab-updated` event with `eventType = "conflict_detected"`:
  - Show a persistent dismissible warning banner at the top of the tab view.
  - List each conflicting item: "'{itemName}' was removed from this booking in ROLLER. Review and adjust the tab manually."
  - Banner persists until dismissed by the operator (or tab is settled).
- On `tab-updated` event with `eventType = "booking_updated"` (no conflict):
  - Show a transient toast: "Booking updated — tab totals refreshed."
  - Refresh the tab view.

**ROLLER Endpoints Used:**

| Endpoint | Status |
|----------|--------|
| POST (inbound) /api/webhooks/roller | New — real (ROLLER POSTs to POS) |
| GET /bookings/{id} | Real |

**Effort:** 2–3 days

### Phase 4 Approval Gate

> **Demonstrate before Phase 5 begins:**
> - ngrok is running; the webhook endpoint is registered with ROLLER.
> - Amend a live ROLLER booking (e.g. change guest count) while the POS has an open tab for that booking.
> - Confirm the POS tab view shows a toast notification within a few seconds — no page refresh needed.
> - Trigger a conflicting amendment (remove a line item that's on the tab).
> - Confirm the conflict banner appears and the tab is not automatically modified.
> - Confirm that a webhook for a booking with no open tab is silently acknowledged (check logs).
> - Confirm that an invalid webhook signature returns `401` and is logged.

---

## Phase 5 — Payment, Settlement, and Receipt Generation

**Goal:** Take payment (simulated card or real ROLLER gift card), settle the tab, release the lock, and display an Australian Tax Invoice receipt.

### Tasks

#### T5.1 — Frontend: payment UI

Accessible from the open tab view. Two payment paths:

**Card payment:**
- Button: "Pay by Card"
- On click: call `POST /api/tabs/{tabId}/settle/card`
- Backend generates a new random 16-digit card number (distinct from the pre-auth card), processes settlement, returns `{ cardNumber, receiptData }`.
- Display the card number briefly: "Charged to card XXXX-XXXX-XXXX-{last4}" then navigate to receipt.

**Gift card payment:**
- Button: "Pay by Gift Card"
- Text input: gift card number
- "Check Balance" button → `GET /api/tabs/{tabId}/giftcard-balance?cardNumber=`
- Display available balance; if insufficient, show shortfall and offer partial payment option (out of scope for prototype — show error "Insufficient balance on gift card").
- "Confirm Payment" button → `POST /api/tabs/{tabId}/settle/gift-card`

**Split payment:** Not required for this prototype. If the gift card balance is less than the tab total, show an error.

#### T5.2 — Backend: card settlement

`POST /api/tabs/{tabId}/settle/card`

1. Validate tab is `open`.
2. Generate new random 16-digit settlement card number.
3. Call ROLLER push charges endpoint (Phase 6 — T6.1) — but in Phase 5, stub this call if Phase 6 is not yet complete (log the would-be payload, return success).
4. Call ROLLER payment unlock endpoint (`DELETE /bookings/{id}/payment-lock`).
5. If unlock fails: log the failure, do not block settlement. Record a `rollerPushStatus = "unlock_failed"` flag. Operator must manually release via admin endpoint.
6. Persist `Payment` record: `type = "settlement"`, `method = "card"`, `cardNumber = generated`, `status = "success"`, `rollerPushStatus = "pending"` (updated to `"pushed"` after Phase 6 completes).
7. Update `tab.PaymentStatus = "settled"`, `tab.SettledAt = now`.
8. Return receipt data (see T5.4).

On any failure before step 7: return error, do not modify the tab or ROLLER booking. Lock remains. (Aligns with acceptance criteria: "On payment failure: ROLLER booking is not modified, lock remains.")

#### T5.3 — Backend: gift card settlement

`POST /api/tabs/{tabId}/settle/gift-card` body: `{ "cardNumber": "string" }`

1. Validate tab is `open`.
2. Call ROLLER gift card balance lookup (`GET /gift-cards/{cardNumber}/balance`). If insufficient or card is not `active`, return `422`.
3. Call ROLLER gift card deduction (`POST /gift-cards/{cardNumber}/deduct`) with the tab's `grandTotal`.
4. If deduction fails (race condition — balance changed between lookup and deduction), return `422`.
5. Store the ROLLER `transactionId` returned from deduction.
6. Proceed with steps 4–8 from T5.2 (unlock, persist payment, update tab).
7. Persist `Payment` record: `type = "gift_card"`, `method = "gift_card"`, `cardNumber = cardNumber`, `status = "success"`.

#### T5.4 — Backend: receipt generation

Add a `ReceiptService` that computes the receipt data from the settled tab:

```csharp
public record ReceiptData(
    string TabId,
    string VenueName,
    string AbnPlaceholder,
    DateTime IssuedAt,
    IEnumerable<ReceiptLineItem> LineItems,
    decimal SubtotalExclGst,
    decimal GstTotal,
    decimal GrandTotal,
    string PaymentMethod,
    string PaymentReference // masked card or gift card number
);

public record ReceiptLineItem(
    string Name,
    int Quantity,
    decimal UnitPriceInclGst,
    decimal GstAmount,
    decimal LineTotal
);
```

`GET /api/tabs/{tabId}/receipt` — returns `ReceiptData` JSON. Also embedded in the settlement response.

GST calculation: `gstAmount = lineTotal - (lineTotal / 1.1)` (Australian 10% GST, amounts rounded to nearest cent).

#### T5.5 — Frontend: receipt view

Route: `/receipt/{tabId}`

An on-screen Australian Tax Invoice:

```
┌─────────────────────────────────────┐
│            TAX INVOICE              │
│                                     │
│  {Venue Name}                       │
│  ABN: {ABN Placeholder}             │
│  {Date} {Time}                      │
│  Receipt #: {tabId short}           │
│─────────────────────────────────────│
│  DESCRIPTION    QTY  UNIT   TOTAL   │
│  {item name}      2  $X.XX  $X.XX   │
│  ...                                │
│─────────────────────────────────────│
│                 Subtotal:  $XX.XX   │
│                 GST (10%): $ X.XX   │
│                 TOTAL:     $XX.XX   │
│─────────────────────────────────────│
│  Payment: {method} {masked number}  │
│  Status: PAID                       │
└─────────────────────────────────────┘
```

"Tax Invoice" must appear as the page title/header per ATO requirements (illustrative only). "New Tab" button returns to the home screen.

**ROLLER Endpoints Used:**

| Endpoint | Status |
|----------|--------|
| GET /gift-cards/{id}/balance | Real (new endpoint — stub if not yet built) |
| POST /gift-cards/{id}/deduct | Real (new endpoint — stub if not yet built) |
| DELETE /bookings/{id}/payment-lock | Real (built in Phase 3) |
| POST /bookings/{id}/fnb-charges | Stubbed in Phase 5; real in Phase 6 |

**Effort:** 3–4 days

### Phase 5 Approval Gate

> **Demonstrate before Phase 6 begins:**
> - Card settlement: settle an open tab, confirm receipt appears with correct line items, GST breakdown, and masked card number.
> - Gift card settlement: enter a gift card number, check balance, confirm deduction, confirm receipt shows gift card as payment method.
> - Confirm `Payment` record in database has `type = "settlement"` and `status = "success"`.
> - Attempt a settlement with an insufficient gift card balance — confirm error message, tab remains open, lock remains.
> - Simulate a lock unlock failure (toggle stub to return 500) — confirm settlement still completes and `rollerPushStatus = "unlock_failed"` is recorded.
> - Receipt displays correctly at `/receipt/{tabId}` with "Tax Invoice" header.

---

## Phase 6 — Payment Sync and Final ROLLER State

**Goal:** Ensure ROLLER's booking record accurately reflects all POS charges after settlement.

### Tasks

#### T6.1 — Backend: push F&B charges to ROLLER

Called from the settlement flow (T5.2 / T5.3, step 3 — remove the stub).

`RollerChargeService.PushFnbChargesAsync(Tab tab, Payment payment)`:

1. Build the `fnb-charges` request payload from `tab.addedItemsJson` (F&B items added during the visit) — `importedItemsJson` items were already on the booking.
2. Generate an `idempotencyKey` = `{tabId}-settlement` (stable, safe to retry).
3. Call `POST /api/v1/bookings/{bookingId}/fnb-charges` via `IRollerApiClient`.
4. On success: update `payment.RollerPushStatus = "pushed"` in the database.
5. On failure: update `payment.RollerPushStatus = "failed"`, log the full payload for manual retry. Do not fail the settlement — the tab is already settled; this is a reconciliation-side concern.
6. On `409 duplicate_request` (idempotency conflict): treat as success, update `RollerPushStatus = "pushed"`.

#### T6.2 — Backend: update ROLLER booking status

After pushing F&B charges, call ROLLER's Update Booking endpoint to mark the booking as paid/finalised:
- Confirm the exact field(s) to write with the ROLLER team (e.g. `paymentStatus`, `customFields.posSettledAt`).
- If ROLLER's Update Booking endpoint does not support a "paid" flag, skip this step and note it as a production gap.

#### T6.3 — Frontend: ROLLER sync status indicator

In the receipt view and tab history:
- Show a "Synced to ROLLER" green badge if `rollerPushStatus = "pushed"`.
- Show an "ROLLER Sync Pending" amber badge if `rollerPushStatus = "not_pushed"` or `"failed"`.
- The amber badge includes a "Retry Sync" button that calls `POST /api/tabs/{tabId}/retry-push`.

#### T6.4 — Backend: retry push endpoint

`POST /api/tabs/{tabId}/retry-push` — re-runs `PushFnbChargesAsync` for a tab whose `rollerPushStatus` is not `"pushed"`. Idempotency key ensures the retry is safe.

**ROLLER Endpoints Used:**

| Endpoint | Status |
|----------|--------|
| POST /bookings/{id}/fnb-charges | Real (new endpoint) |
| PUT /bookings/{id} | Real |

**Effort:** 2–3 days

### Phase 6 Approval Gate

> **Demonstrate before Phase 7 begins:**
> - Complete a full settlement. Inspect the ROLLER booking via the ROLLER portal or API — confirm it shows the correct F&B charges posted to it.
> - Confirm the POS receipt and ROLLER booking show identical line items and totals.
> - Simulate a charge push failure — confirm `rollerPushStatus = "failed"` is recorded and the amber badge appears.
> - Use "Retry Sync" — confirm the push succeeds and the badge turns green.

---

## Phase 7 — Simulate Till Close and Polish

**Goal:** Walk-out scenario, till-close automation, and full prototype polish.

### Tasks

#### T7.1 — Backend + frontend: walk-out flagging

`POST /api/tabs/{tabId}/walkout` — sets `tab.PaymentStatus = "walkout_pending"`. No lock is released; the stored `preAuthCardNumber` will be used for the walkout charge.

Frontend: "Mark as Walk-Out" button in the tab view, behind a confirmation dialog ("This will flag the tab as a walk-out. The stored pre-auth card will be charged at till close.").

Walkout-flagged tabs show a red "WALK-OUT" badge in the tab list.

#### T7.2 — Backend: till-close logic

`Services/TillCloseService.cs`:

```csharp
public async Task<TillCloseResult> RunTillCloseAsync()
{
    var walkoutTabs = await _db.Tabs
        .Where(t => t.PaymentStatus == "walkout_pending")
        .ToListAsync();

    var results = new List<TabCloseOutcome>();
    foreach (var tab in walkoutTabs)
    {
        try
        {
            // Attempt charge using stored preAuthCardNumber
            var payment = new Payment {
                TabId = tab.TabId,
                Type = "walkout_charge",
                Method = "card",
                CardNumber = tab.PreAuthCardNumber,
                Amount = tab.GrandTotal,
                Currency = "AUD",
                Status = "success",   // simulated — always succeeds
                RollerPushStatus = "not_pushed"
            };
            _db.Payments.Add(payment);
            tab.PaymentStatus = "settled";
            tab.SettledAt = DateTime.UtcNow;
            // Attempt ROLLER push (non-blocking on failure)
            await _chargeService.PushFnbChargesAsync(tab, payment);
            results.Add(new(tab.TabId, "success"));
        }
        catch (Exception ex)
        {
            tab.PaymentStatus = "failed";
            results.Add(new(tab.TabId, "failed", ex.Message));
        }
    }
    await _db.SaveChangesAsync();
    return new TillCloseResult(results);
}
```

`POST /api/admin/till-close` — calls `TillCloseService.RunTillCloseAsync()`. Returns a summary of all processed tabs.

#### T7.3 — Frontend: "Simulate Till Close" button

Prominent button in the top navigation bar, styled distinctively (amber or red-orange). Gated behind a confirmation dialog: "This will attempt to charge all walk-out tabs. This action cannot be undone."

On confirm: calls `POST /api/admin/till-close`, displays a results modal:

```
Till Close Summary — 3am Simulation
─────────────────────────────────────
Tab #abc123 (John Smith)   CHARGED  $42.50
Tab #def456 (Jane Doe)     CHARGED  $18.00
Tab #ghi789 (Bob Jones)    FAILED   [error detail]
─────────────────────────────────────
2 charged successfully | 1 failed
```

Failed tabs remain visible in the tab list with a "CHARGE FAILED" badge. A "Retry Charge" button is available per failed tab.

#### T7.4 — Polish sweep

Apply to all phases:

- **Loading states:** `mat-spinner` or skeleton placeholders on all async operations.
- **Error states:** Every API call has a visible, non-crashing error state. The `GlobalExceptionMiddleware` (T1a.1) ensures no blank 500 pages.
- **Empty states:** Catalogue with no F&B products, search with no results, tab with no items — all have clear empty-state messages.
- **Confirmation dialogs:** Walk-out, till close, force release, close tab — all require confirmation.
- **Tab status badges:** Color-coded status chips throughout: `open` (blue), `settled` (green), `failed` (red).
- **Responsiveness:** Ensure the POS is usable on a 1366×768 tablet landscape (typical venue tablet). Not mobile.
- **ROLLER API error handling:** Every `RollerApiException` produces a visible, descriptive toast. The exception must include the ROLLER error body for debuggability.

#### T7.5 — Acceptance criteria run-through

Walk through the full acceptance checklist (see below) and document pass/fail for each item. Fix any failures before sign-off.

**ROLLER Endpoints Used:**

| Endpoint | Status |
|----------|--------|
| POST /bookings/{id}/fnb-charges | Real |
| DELETE /bookings/{id}/payment-lock | Real (via settle-on-walkout path) |

**Effort:** 2–3 days

### Phase 7 Approval Gate — Final Sign-Off

> **Full end-to-end demo covering:**
> 1. **Happy path:** Search booking → import → add F&B items → card payment → receipt → confirm ROLLER sync.
> 2. **Gift card path:** Same flow but pay with a ROLLER gift card. Confirm balance lookup and deduction.
> 3. **Walk-out scenario:** Import a booking, add items, mark as walk-out, trigger till close, confirm charge recorded and ROLLER booking updated.
> 4. **Webhook live demo:** Amend a booking in ROLLER while the tab is open — confirm the POS updates in real time.
> 5. **Conflict demo:** Remove a line item from a booking in ROLLER that's already on the tab — confirm conflict banner in POS.
> 6. **Acceptance criteria checklist signed off** (all 16 items).

---

## Acceptance Criteria Checklist

| # | Criterion | Phase |
|---|-----------|-------|
| 1 | Operator can search for a booking by name, email, or booking ID | 2 |
| 2 | Full booking state (tickets + F&B) is imported and displayed on the tab | 2 |
| 3 | F&B product catalogue loads from ROLLER and is browsable | 1b |
| 4 | Operator can add F&B items to an open tab | 1c |
| 5 | Non-payment booking fields (time, capacity) can be amended on a locked booking | 3 |
| 6 | `booking_updated` webhook reflects ROLLER-side changes on the open tab in real time | 4 |
| 7 | On booking import, a random card number is generated and displayed; pre-auth record persisted | 2 |
| 8 | At payment, a random card number is generated and displayed; payment record persisted | 5 |
| 9 | ROLLER gift card balance lookup and deduction work at payment | 5 |
| 10 | On payment success: F&B items pushed to ROLLER, lock released, unified receipt displayed | 5, 6 |
| 11 | On payment failure: ROLLER booking not modified, lock remains | 5 |
| 12 | Walk-out scenario: simulated pre-auth charge attempted; result persisted; failure flagged | 7 |
| 13 | "Simulate till close" triggers auto-closure and charges all open walk-out tabs | 7 |
| 14 | Receipt renders as an Australian Tax Invoice | 5 |
| 15 | All payment records (pre-auth, settlement, walk-out) are stored and retrievable | 1a, 2, 5, 7 |
| 16 | All ROLLER API errors produce a visible, non-crashing error state | 1a, polish |

---

## Effort Summary

| Phase | Description | Estimate |
|-------|-------------|----------|
| 0 | API discovery and contract design | 2–3 days |
| 1a | Project scaffold | 1–2 days |
| 1b | Product catalogue | 1–2 days |
| 1c | Tab management | 1–2 days |
| 2 | Booking search, import, pre-auth | 2–3 days |
| 3 | Payment lock (incl. ROLLER-side + stub) | 3–4 days |
| 4 | Webhook handling | 2–3 days |
| 5 | Payment, settlement, receipt | 3–4 days |
| 6 | ROLLER sync and final state | 2–3 days |
| 7 | Till close and polish | 2–3 days |
| **Total** | | **19–29 days** |

> These estimates assume one full-stack developer. Phase 3 is partially parallelisable with the ROLLER backend team; Phase 0 requires ROLLER team availability for contract sign-off.

---

## Critical Path and Dependencies

```
Phase 0 ──► Phase 1 ──► Phase 2 ──► Phase 3 ──► Phase 4
                                         │
                                         ▼
                                     Phase 5 ──► Phase 6 ──► Phase 7
```

- **Phase 0 approval blocks everything.** F&B enum values are needed in Phase 1b; webhook signature mechanism is needed in Phase 4; lock contract is needed in Phase 3.
- **Phase 3 has a ROLLER-side dependency.** The POS stub (T3.2) enables parallel POS development; full integration requires the real ROLLER endpoint.
- **Phase 5 gift card path** depends on the ROLLER gift card endpoints being built. If they lag, stub them in Phase 5 and integrate in Phase 6.
- **Phase 6** can begin as soon as Phase 5's card settlement path is approved, without waiting for gift card endpoints.
