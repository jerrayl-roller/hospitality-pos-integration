# Phase 1 Execution Prompt — Working POS with Product Catalogue and Tabs

## What you are building

A whitelabelled, browser-based F&B point-of-sale (POS) prototype that integrates with the ROLLER venue management platform. This is a prototype — not a production system. The goal is to demonstrate the integration end-to-end.

**Phase 1 goal:** A working browser-based POS that loads the F&B product catalogue from the real ROLLER API and lets an operator create and manage a tab with items and a running total. No booking integration in this phase.

---

## Project context

The project root is `C:/source/roller/prototype-hospitality-pos/`. The following already exists:

```
prototype-hospitality-pos/
├── .env                          # ROLLER_CLIENT_ID, ROLLER_CLIENT_SECRET, ADMIN_TOKEN
├── .env.example
├── .gitignore
└── docs/
    └── api-contracts/
        ├── CONTRACTS.md
        ├── existing-endpoints.md # confirmed ROLLER API field names, enums, auth
        ├── payments.md
        ├── payment-lock.md
        ├── push-charges.md
        ├── gift-card.md
        └── webhook.md
```

Create the application code under two new directories:

```
prototype-hospitality-pos/
├── pos-backend/      # .NET 9 Web API
└── pos-frontend/     # Angular app
```

---

## Tech stack

- **Frontend:** Angular (latest stable), TypeScript, Angular Material — no Tailwind
- **Backend:** .NET 9, C#, ASP.NET Core Web API
- **Database:** SQL Server, Entity Framework Core (code-first migrations)
- **Styling:** Angular Material or standard CSS

---

## ROLLER API — confirmed details

Read `docs/api-contracts/existing-endpoints.md` for the full reference. Key facts for Phase 1:

### Authentication — OAuth 2.0 client credentials

```
POST https://api.roller.app/token
Content-Type: application/json

{ "client_id": "...", "client_secret": "..." }
```

Response:
```json
{ "access_token": "...", "token_type": "Bearer", "expires_in": 86400 }
```

All API requests use `Authorization: Bearer {access_token}` and `Accept: application/json`.

**Token caching rules (important):**
- Cache the token in `IMemoryCache`. Do not request a new token per API call.
- Evict from cache at `expires_in - 60` seconds (read `expires_in` from the response — do not hardcode 86400).
- On any `401` response from a ROLLER endpoint: discard the cached token, fetch a new one, and retry the original request once.
- Credentials come from environment variables `ROLLER_CLIENT_ID` and `ROLLER_CLIENT_SECRET` (already in `.env`).

### Product catalogue endpoint

```
GET https://api.roller.app/data/products
Authorization: Bearer {access_token}
```

Query params: `pageNumber` (default 1), `pageSize` (default 100).

**Response fields relevant to the POS:**

| Field | Type | Notes |
|-------|------|-------|
| `productId` | string | e.g. `"983361"` |
| `name` | string | Display name |
| `price` | number or null | **Decimal float** (e.g. `11.00`) — not cents |
| `productType` | string | e.g. `"AddOn"` |
| `productSubType` | string | e.g. `"Stock"` |
| `productStatus` | string | `Draft` \| `Published` \| `Closed` \| `Archived` |

**No image URL field and no currency field** in the product response.

### F&B filter

All products should be stored in the local database after syncing (to be able to correlate with ROLLER bookings and generate receipts), but only the following should be displayed on the POS
- `productType == "AddOn"` (integer value `7` — the API returns the string name)
- `productStatus == "Published"`
- `productSubType` == `"Stock"`


**Fetch all pages:** The endpoint paginates with `pageSize` (default 100). Fetch all pages server-side before caching and returning to the frontend.

---

## Phase 1 subphases

### Phase 1a — Project scaffold

#### Backend (`pos-backend/PosApi/`)

Scaffold with:
```
dotnet new webapi -n PosApi --framework net9.0
```

**NuGet packages to add:**
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Microsoft.EntityFrameworkCore.Tools`
- `Swashbuckle.AspNetCore`
- `Microsoft.AspNetCore.SignalR` — scaffold the hub now even though it is wired up in Phase 4

**`appsettings.json`:**
```json
{
  "ConnectionStrings": {
    "Pos": "Server=localhost;Database=RollerPos;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Roller": {
    "BaseUrl": "https://api.roller.app",
    "ClientId": "",
    "ClientSecret": "",
    "TokenEndpoint": "https://api.roller.app/token"
  },
  "AdminToken": ""
}
```

Load `ROLLER_CLIENT_ID`, `ROLLER_CLIENT_SECRET`, and `ADMIN_TOKEN` from environment variables at startup. Use `dotenv.net` or read the `.env` file manually — do not commit credentials. Map them onto the `Roller` config section.

**CORS:** Allow `http://localhost:4200` in development.

**`GlobalExceptionMiddleware`:** Catch all unhandled exceptions and return:
```json
{ "error": "internal_server_error", "detail": "..." }
```
Never let blank 500 responses reach the frontend.

#### EF Core data models

**`Tab` entity:**
```csharp
public class Tab
{
    public Guid TabId { get; set; }
    public string? BookingId { get; set; }          // null in Phase 1; populated in Phase 2
    public string ImportedItemsJson { get; set; } = "[]";  // Phase 2 — booking line items
    public string AddedItemsJson { get; set; } = "[]";     // Phase 1 — F&B items added at POS
    public string AuditLogJson { get; set; } = "[]";       // Phase 4 — webhook conflict log
    public decimal GrandTotal { get; set; }
    public string? PreAuthCardNumber { get; set; }          // Phase 2
    public string PreAuthStatus { get; set; } = "none";     // Phase 2
    public string PaymentStatus { get; set; } = "open";     // open|settled|failed|walkout_pending
    public string? RollerLockId { get; set; }               // Phase 3
    public bool StuckLock { get; set; }                     // Phase 3
    public bool HasPendingConflict { get; set; }            // Phase 4
    public DateTime OpenedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
```

**`Payment` entity:**
```csharp
public class Payment
{
    public Guid PaymentId { get; set; }
    public Guid TabId { get; set; }
    public Tab Tab { get; set; } = null!;
    public string Type { get; set; } = "";      // pre_auth|settlement|walkout_charge|gift_card
    public string Method { get; set; } = "";    // card|gift_card
    public string? CardNumber { get; set; }     // 16-digit simulated or gift card number
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Status { get; set; } = "";    // pending|success|failed
    public string RollerPushStatus { get; set; } = "not_pushed"; // not_pushed|pushed|failed
    public string? RollerGiftCardTransactionId { get; set; }     // Phase 5
    public DateTime CreatedAt { get; set; }
}
```

Run migrations:
```
dotnet ef migrations add InitialCreate
dotnet ef database update
```

#### ROLLER API client

Create `Services/Roller/RollerApiClient.cs` — a thin transport wrapper. No business logic here.

```csharp
public interface IRollerApiClient
{
    Task<T> GetAsync<T>(string path, CancellationToken ct = default);
    Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default);
    Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default);
    Task DeleteAsync(string path, object? body = null, CancellationToken ct = default);
}
```

Implementation requirements:
- Registered via `IHttpClientFactory` with `BaseAddress = config["Roller:BaseUrl"]`.
- Before every request, retrieve a valid access token from `RollerTokenService` (see below).
- Set `Authorization: Bearer {token}` and `Accept: application/json` on every request.
- On `401`: call `RollerTokenService.InvalidateToken()`, fetch a new token, retry the request **once**. If it fails again, throw.
- On any non-2xx: deserialise the ROLLER error body and throw a typed `RollerApiException(int statusCode, string error, string detail)`.
- Never expose `HttpResponseMessage` or raw HTTP types outside this class.

Create `Services/Roller/RollerTokenService.cs`:
- Calls `POST /token` with `client_id` + `client_secret` from config.
- Caches the `access_token` in `IMemoryCache` with expiry of `expires_in - 60` seconds.
- `InvalidateToken()` removes the cache entry so the next call fetches a fresh token.

#### Health check endpoint

`GET /api/health`:
- Attempts to fetch a ROLLER token (validates credentials work).
- Returns `{ "status": "ok", "rollerConnected": true }` on success.
- Returns `{ "status": "degraded", "rollerConnected": false, "error": "..." }` on failure.
- Does **not** call any booking or product endpoints — token fetch is sufficient.

---

#### SignalR hub (scaffold only — wired up in Phase 4)

Create `Hubs/TabHub.cs`:
```csharp
public class TabHub : Hub
{
    public async Task JoinTab(string tabId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tab-{tabId}");
}
```

Map it in `Program.cs` at `/hubs/tab` but do not implement any broadcast logic yet.

#### Health check endpoint

`GET /api/health`:
- Attempts to fetch a ROLLER token (validates credentials work).
- Returns `{ "status": "ok", "rollerConnected": true }` on success.
- Returns `{ "status": "degraded", "rollerConnected": false, "error": "..." }` on failure.
- Does **not** call any booking or product endpoints — token fetch is sufficient.

---

### Phase 1b — Product catalogue

#### Backend

Create `Services/Roller/ProductService.cs`:

```csharp
public interface IProductService
{
    Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken ct = default);
}
```

Implementation:
1. Fetch all pages from `GET /data/products` via `IRollerApiClient` until no more results (increment `pageNumber` until a page returns fewer items than `pageSize`).
2. Map to `ProductDto`:
   ```csharp
   public record ProductDto(
       string ProductId,
       string Name,
       decimal Price,         // decimal float as returned by ROLLER
       string ProductType,
       string ProductSubType
   );
   ```
3. Filter for display:
   - `productType == "AddOn"`
   - `productStatus == "Published"`
   - `productSubType` == `"Stock"`

Controller: `GET /api/products/fnb` — returns the list. No pagination needed on this endpoint (the frontend receives all F&B products in one response).

**Error handling:** If the ROLLER API call fails, return a `502` with a structured error body. Do not return an empty list silently.

#### Frontend (`pos-frontend/`)

Scaffold:
```
ng new pos-frontend --routing --style=scss --ssr=false
ng add @angular/material
npm install @microsoft/signalr   # install now — used in Phase 4
```

**`environment.ts`:**
```typescript
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000'
};
```

**`ApiService`** (`core/api.service.ts`): wraps `HttpClient`, prepends `environment.apiBaseUrl` to all paths.

**`ErrorInterceptor`** (`core/error.interceptor.ts`): catches HTTP errors, extracts the `error` field from the JSON body, dispatches to `NotificationService` (Angular Material `MatSnackBar`).

**Shell layout:** Angular Material `mat-sidenav-container` — left nav with links (Catalogue, Search Booking [Phase 2], Admin [Phase 3]), main content area on the right.

**Catalogue route (`/catalogue`):**
- On load: call `GET /api/products/fnb`.
- Display category filter tabs at the top using distinct `category` values sorted alphabetically.
- Product grid: `mat-card` per product showing name, price (formatted as AUD currency), category badge, and "Add to Tab" button (disabled if no tab is open).
- Loading skeleton while fetching.
- Error state with a "Retry" button if the API call fails.

---

### Phase 1c — Tab management

#### Backend

**Endpoints:**

```
POST   /api/tabs                         — create a new empty tab
GET    /api/tabs/{tabId}                 — get tab detail with payments
POST   /api/tabs/{tabId}/items           — add or increment an item
DELETE /api/tabs/{tabId}/items/{productId} — decrement or remove an item
DELETE /api/tabs/{tabId}                 — delete an empty, unpaid tab
```

`POST /api/tabs` — creates a new tab with `PaymentStatus = "open"`, `OpenedAt = UtcNow`. No body required for Phase 1 (Phase 2 will add `bookingId`). Returns the full `Tab` record.

`POST /api/tabs/{tabId}/items` body:
```json
{
  "productId": "string",
  "productName": "string",
  "quantity": 1,
  "unitPrice": 11.00
}
```
Logic: deserialise `AddedItemsJson`, find existing entry by `productId` and increment quantity, or add a new entry. Re-serialise and save. Recompute `GrandTotal` as the sum of `unitPrice × quantity` across all added items. Return the updated tab.

`DELETE /api/tabs/{tabId}/items/{productId}` — decrement quantity by 1; remove the entry when quantity reaches 0. Recompute `GrandTotal`. Return the updated tab.

`DELETE /api/tabs/{tabId}` — only allowed when `AddedItemsJson == "[]"` and `PaymentStatus == "open"`. Return `409` otherwise.

**Prevent duplicate open tabs:** For Phase 1 there is no booking constraint. Allow multiple open tabs (each tab is independent until Phase 2 adds the one-tab-per-booking rule).

#### Frontend

**`TabStateService`** (`core/tab-state.service.ts`): holds the current open `tabId` in a `BehaviorSubject<string | null>`. Persists to `sessionStorage` so a page refresh does not lose the active tab.

**Tab drawer** — persistent `mat-drawer` on the right side of the shell, opens when a tab is active:
- "Open New Tab" button in the top bar — calls `POST /api/tabs`, stores the `tabId` in `TabStateService`, opens the drawer.
- Line items table: product name, quantity, unit price, line total.
- Running grand total at the bottom.
- "+ / −" quantity buttons per item — call the add/remove item endpoints. The "Add to Tab" button on catalogue cards calls the same add endpoint.
- "Close Tab" button (only shown when the tab has no items) — calls `DELETE /api/tabs/{tabId}`, clears `TabStateService`.
- All tab mutations refresh the tab state from the server response (do not optimistically update client-side).

---

## What NOT to build in Phase 1

The following belong to later phases. Do not implement them now, but scaffold anything that later phases will need to extend (e.g. the `BookingId` column on `Tab`, the `SignalR` hub registration):

| Feature | Phase |
|---------|-------|
| Booking search and import | 2 |
| Pre-auth card simulation | 2 |
| Payment lock / unlock | 3 |
| Admin force-release endpoint | 3 |
| Webhook endpoint and processing | 4 |
| SignalR broadcast logic | 4 |
| Card and gift card settlement | 5 |
| Receipt generation | 5 |
| Push F&B charges to ROLLER | 6 |
| Walk-out flagging and till-close | 7 |

---

## Cross-phase dependencies built in Phase 1

These decisions made in Phase 1 are load-bearing for later phases — do not cut corners on them:

- **`RollerApiClient` and `RollerTokenService`** — every subsequent phase calls ROLLER through these. The OAuth2 token caching and 401-retry logic must be correct now.
- **`Tab` and `Payment` EF Core entities** — include all columns listed above, even those used only in later phases (`BookingId`, `RollerLockId`, `StuckLock`, `HasPendingConflict`, etc.). Adding columns via migrations later is fine, but the schema should be designed with the full data model in mind.
- **`AddedItemsJson` line item format** — this exact structure is consumed by Phase 6 (`push-charges`) and Phase 7 (till-close). Use this shape:
  ```json
  [
    {
      "productId": "983361",
      "productName": "Craft Beer",
      "quantity": 2,
      "unitPrice": 11.00
    }
  ]
  ```
- **`GrandTotal` as decimal** — stored as `decimal` in SQL Server (not `float`). Recomputed from `AddedItemsJson` on every item mutation.
- **Angular `TabStateService`** — Phase 2 will extend this to hold the full tab object (including `bookingId`, `preAuthCardNumber`, `paymentStatus`). Design it to hold `Tab | null`, not just `tabId | null`.
- **SignalR hub** — registered in `Program.cs` at `/hubs/tab` now so Phase 4 can add broadcast logic without touching the registration.

---

## Approval gate — what must be demonstrated before Phase 2 begins

1. Browser at `http://localhost:4200/catalogue` shows a product grid loaded from the real ROLLER API, filtered to F&B `AddOn` products, grouped by `reportingCategoryName`.
2. Category filter tabs change which products are shown.
3. "Open New Tab" button creates a tab and opens the drawer.
4. "Add to Tab" on a catalogue card adds the item to the drawer with the correct price.
5. "+ / −" controls update quantity and grand total correctly.
6. Grand total matches the sum of `quantity × unitPrice` across all items.
7. Tab state persists across a page refresh (session storage).
8. "Close Tab" removes an empty tab and closes the drawer.
9. Pulling the network cable and reloading shows an error state in the catalogue with a Retry button — no blank screen or unhandled exception.
10. `GET /api/health` returns `rollerConnected: true`.
11. SQL Server contains a `Tabs` and `Payments` table with the correct columns (verify via SSMS or `dotnet ef dbcontext info`).
