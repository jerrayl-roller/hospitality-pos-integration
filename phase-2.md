# Phase 2 Implementation Prompt — Booking Search, Import, and Pre-Auth Simulation

## Context

This is a continuation of a phased F&B POS prototype integrating with the ROLLER venue management platform. Phase 1 is complete and working.

**Stack:** Angular 19 (standalone components, no NgModule) · .NET 9 Web API · SQL Server + EF Core 9 · Angular Material 21.x (Material 3)

**Run commands:**
```bash
# Backend (from pos-backend/PosApi/)
dotnet run --launch-profile http      # http://localhost:5000

# Frontend (from pos-frontend/)
ng serve                               # http://localhost:4200
```

**ROLLER environment:** `https://api.roller.local`  
**Authentication:** OAuth2 client credentials. The backend has a working `RollerTokenService` (`pos-backend/PosApi/Services/Roller/RollerTokenService.cs`) that fetches and caches bearer tokens automatically. All ROLLER calls go through `IRollerApiClient` — never call ROLLER from controllers directly.

---

## What Phase 1 Built (Existing Code to Reuse)

### Backend (`pos-backend/PosApi/`)

- **`Models/Tab.cs`** — Full entity already includes `BookingId`, `ImportedItemsJson`, `PreAuthCardNumber`, `PreAuthStatus`, `StuckLock`, `GuestName`, `GuestEmail`, `GuestPhone`, and all payment status fields.
- **`Models/Payment.cs`** — Full entity with `Type`, `Method`, `CardNumber`, `Amount`, `Status`, `RollerPushStatus`.
- **`Data/PosDbContext.cs`** — EF context with `Tabs` and `Payments` DbSets.
- **`Services/TabService.cs`** — Has `CreateTabAsync(CreateTabRequest req)`, `AddItemAsync`, `RemoveItemAsync`, `GetTabAsync`, `GetAllTabsAsync`, `DeleteTabAsync`, `RestoreItemsAsync`.
- **`Controllers/TabsController.cs`** — Full tab CRUD.
- **`Services/Roller/RollerApiClient.cs`** — `IRollerApiClient` with `GetAsync<T>`, `PostAsync<T>`. Add any additional HTTP verbs if needed.
- **`Services/Roller/ProductService.cs`** — Example of how to call ROLLER and cache the result.
- **`Dtos/TabDtos.cs`** — `TabDto`, `TabSummaryDto`, `CreateTabRequest`, `AddItemRequest`, `TabLineItem`.

### Frontend (`pos-frontend/src/app/`)

- **`core/api.service.ts`** — `ApiService` with `get<T>`, `post<T>`, `put<T>`, `delete<T>`.
- **`core/tab-state.service.ts`** — `TabStateService` with `openNewTab()`, `refreshTab(tabId)`, `addItem()`, `removeItem()`, `parkTab()`, `discardChanges()`, `hasChanges`, `isExistingTab`. `refreshTab()` is the correct method to call after importing a booking — it loads the tab as the active tab and sets a snapshot for discard tracking.
- **`core/notification.service.ts`** — `NotificationService` with `showError(message)` and `showSuccess(message)` for snackbar toasts.
- **`features/tab/new-tab-dialog.ts`** — Example `MatDialog` usage with Reactive Forms.
- **`app.component.ts/.html/.scss`** — Shell layout with left nav (`mat-nav-list`), top bar, main content area, and right tab panel.

---

## Phase 2 Goal

Operator can search for a real ROLLER booking, import it as an active tab (with pre-existing booking items loaded), see a simulated pre-auth card number, and be ready to add F&B items. Duplicate booking imports are blocked.

---

## Tasks

### T2.1 — Backend: Booking Search

**New file:** `pos-backend/PosApi/Services/Roller/BookingService.cs`

Create `IBookingService` and `BookingService`. The service must:

1. Accept a search query string.
2. Call the ROLLER Search Bookings endpoint. Confirm the exact path and query parameter shape against the live ROLLER API before hardcoding. The ROLLER dev docs reference a search endpoint that accepts name, email, or booking ID — verify the actual HTTP method, path, and request shape by inspecting the ROLLER API directly via `IRollerApiClient`.
3. Map the response to a list of `BookingSummaryDto`:
   ```csharp
   public record BookingSummaryDto(
       string BookingId,
       string? GuestName,
       string? GuestEmail,
       string? BookingDate,
       string? Status,
       decimal TotalAmount,
       int LineItemCount
   );
   ```
4. Return an empty list (not an exception) if ROLLER returns zero results.
5. No caching — search results must be live.

**New file:** `pos-backend/PosApi/Controllers/BookingsController.cs`

```
GET /api/bookings/search?q={query}
```

- Return `400` if `q` is shorter than 3 characters.
- Return `200` with `BookingSummaryDto[]` (empty array for no results).
- Return `500` with structured error body if ROLLER call fails — use the existing `GlobalExceptionMiddleware` pattern.

Register `IBookingService` / `BookingService` in `Program.cs`.

---

### T2.2 — Backend: Booking Import and Tab Creation

**Add to `BookingService`:** A method that fetches full booking detail from ROLLER and creates a `Tab` record.

`POST /api/tabs/from-booking` body: `{ "bookingId": "string" }`

**Add to `TabsController`** (or a new `BookingsController` action if it reads more cleanly):

Steps:
1. **Duplicate check:** Query the database for any tab with `BookingId == req.BookingId` and `PaymentStatus == "open"`. If found, return `409` with `{ "error": "tab_already_open", "existingTabId": "uuid" }`.
2. **Fetch booking detail** from ROLLER Get Booking Detail endpoint. Confirm the exact path from the live API.
3. **Map imported items:** Extract line items from the ROLLER booking response that correspond to F&B / add-on products already on the booking. Map each to a `TabLineItem` (same record used by `AddedItemsJson`). Store in `ImportedItemsJson`. If the booking has no F&B items yet, `ImportedItemsJson = "[]"` is fine — the operator will add items manually.
4. **Fully pre-paid guard:** If the booking's total outstanding balance is zero (confirm the field name from the live API response), return `409` with `{ "error": "booking_fully_prepaid" }`.
5. **Generate pre-auth card number:** `string.Join("-", Enumerable.Range(0, 4).Select(_ => Random.Shared.Next(1000, 9999).ToString()))` — produces format `XXXX-XXXX-XXXX-XXXX`.
6. **Create Tab record:**
   ```csharp
   var tab = new Tab
   {
       TabId = Guid.NewGuid(),
       BookingId = req.BookingId,
       GuestName = /* from booking */,
       GuestEmail = /* from booking */,
       ImportedItemsJson = JsonSerializer.Serialize(importedItems),
       AddedItemsJson = "[]",
       PreAuthCardNumber = preAuthCard,
       PreAuthStatus = "simulated",
       PaymentStatus = "open",
       OpenedAt = DateTime.UtcNow
   };
   ```
7. **Create Payment record:**
   ```csharp
   var payment = new Payment
   {
       PaymentId = Guid.NewGuid(),
       TabId = tab.TabId,
       Type = "pre_auth",
       Method = "card",
       CardNumber = preAuthCard,
       Amount = tab.GrandTotal,
       Currency = "AUD",
       Status = "success",
       RollerPushStatus = "not_pushed",
       CreatedAt = DateTime.UtcNow
   };
   ```
8. Save both to the database in a single `SaveChangesAsync` call.
9. Return the full `TabDto` (same DTO as `GET /api/tabs/{tabId}`), with `preAuthCardNumber` included.

Ensure `TabDto` and `TabSummaryDto` expose `PreAuthCardNumber` and `ImportedItemsJson`/imported items in the response — check `pos-backend/PosApi/Dtos/TabDtos.cs` and add fields if missing.

---

### T2.3 — Frontend: Booking Search Page

**New route:** `/booking-search`  
**New files:** `pos-frontend/src/app/features/booking-search/booking-search.ts`, `booking-search.html`, `booking-search.scss`

Add to the left nav in `app.component.html` (after Catalogue, before Tabs):
```
mat-icon: search
Label: Bookings
routerLink: /booking-search
```

#### Search bar
- `mat-form-field` with a text input, search icon prefix, and "Clear" icon suffix.
- Debounced 300 ms using `rxjs` `debounceTime` + `distinctUntilChanged` — do not fire on every keystroke.
- Do not search if input is fewer than 3 characters; show a hint "Enter at least 3 characters".
- Show `mat-spinner` while the API call is in flight.

#### Results list
Each result is a `mat-card` or list item showing:
- Guest name (bold)
- Booking ID (monospace, small)
- Booking date
- Status badge (colour-coded `mat-chip`: confirmed = green, cancelled = red, other = grey)
- Total amount
- "Import" button (right-aligned, `mat-raised-button color="primary"`)

Empty state: "No bookings found. Try a different name, email, or booking ID."

#### Import flow
When the operator clicks "Import":
1. Call `POST /api/tabs/from-booking`.
2. On success:
   - Call `tabState.refreshTab(tab.tabId)` to set the imported tab as the active tab.
   - Open a `MatDialog` displaying the **Pre-Auth Card modal** (see below).
   - After the dialog is closed, navigate to `/catalogue` so the operator can immediately add items.
3. On `409 tab_already_open`:
   - Show a snackbar: "A tab is already open for this booking."
   - Call `tabState.refreshTab(existingTabId)` to load the existing tab as active.
   - Navigate to `/catalogue`.
4. On `409 booking_fully_prepaid`:
   - Show a snackbar: "This booking has been fully paid. No tab required."
   - Do not navigate.
5. On other errors: show error toast via `NotificationService`.

#### Pre-Auth Card Modal

**New file:** `pos-frontend/src/app/features/booking-search/pre-auth-dialog.ts`

A `MatDialog` component showing:
```
┌─────────────────────────────────────────┐
│  Simulated Pre-Authorisation            │
│─────────────────────────────────────────│
│  Booking imported successfully.         │
│                                         │
│  Pre-Auth Card Number:                  │
│  ┌─────────────────────────────────┐    │
│  │  XXXX-XXXX-XXXX-XXXX           │    │
│  └─────────────────────────────────┘    │
│                                         │
│  This is a simulated card number for    │
│  prototype purposes only.               │
│                                         │
│  [  OK  ]                               │
└─────────────────────────────────────────┘
```

- The card number displayed in a monospace, visually prominent box.
- A single "OK" button closes the dialog.
- `disableClose: true` on the dialog config (operator must acknowledge).

---

### T2.4 — Frontend: Tab State Integration

`TabStateService.refreshTab()` already exists and handles loading an existing tab as the active tab. No changes needed there.

However, the tab panel (`pos-frontend/src/app/features/tab/tab-panel.html`) currently shows only `addedItems`. For imported booking tabs, `importedItems` (from `importedItemsJson`) should also be visible so the operator can see what was already on the booking.

Update `TabDto` on the backend to include a parsed `importedItems: TabLineItem[]` field (same pattern as `addedItems`). Update `tab-state.service.ts` `Tab` interface to include `importedItems: TabLineItem[]`.

In the tab panel, if `tab.importedItems.length > 0`, show a section above `addedItems`:

```
BOOKING ITEMS          (section header, small caps, grey)
─────────────────────────────
  Tea — Indian Spice    x2   $11.00
  Coffee — Flat White   x1   $5.50
─────────────────────────────
TAB ITEMS              (section header)
─────────────────────────────
  [items added during the session]
```

Imported items are read-only (no +/− controls). Added items keep their existing controls.

The grand total shown at the bottom should remain `tab.grandTotal` as computed by the backend (imported + added items combined).

---

## Routing

Add the new route to `pos-frontend/src/app/app.routes.ts`:
```typescript
{ path: 'booking-search', component: BookingSearchComponent }
```

---

## EF Migration

No schema changes are required for Phase 2 — all columns (`BookingId`, `ImportedItemsJson`, `PreAuthCardNumber`, `PreAuthStatus`) already exist on the `Tab` model from Phase 1.

If `GuestPhone` needs to be populated from the ROLLER booking response, it is already a nullable column.

---

## Key Constraints

- **Never call ROLLER directly from controllers.** All ROLLER interaction goes through `IRollerApiClient` via a service.
- **No Angular NgModule.** All components are standalone. Import Angular Material modules directly in each component's `imports` array.
- **No Tailwind.** Angular Material only.
- **ROLLER API shape is unknown until runtime.** Before mapping the booking search or detail response, make a live call and inspect the actual JSON. Do not guess field names — use `JsonElement` or a flexible DTO if the schema is uncertain, and adjust once confirmed.
- **Pre-auth is always simulated.** No real payment gateway. The card number is random digits — make this clear in the UI.

---

## Acceptance Criteria (Phase 2 Gate)

Before Phase 3 begins, demonstrate:

1. Search for a real ROLLER booking by name, email, and booking ID — all three return results.
2. Import a booking — tab is created in SQL Server with `BookingId` set and `ImportedItemsJson` populated.
3. Pre-auth card number is displayed in the modal immediately after import; it is also retrievable from `GET /api/tabs/{tabId}`.
4. A `Payment` record with `type = "pre_auth"` and `status = "success"` exists in the database for the imported tab.
5. Imported booking items are visible (read-only) in the tab panel; added items appear below them.
6. Attempting to import the same booking twice shows the "tab already open" toast and loads the existing tab.
7. A fully pre-paid booking shows "No tab required" and does not create a tab.
8. Search with fewer than 3 characters shows the hint message and makes no API call.
