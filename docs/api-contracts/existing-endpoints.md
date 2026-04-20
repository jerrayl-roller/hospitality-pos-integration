# Existing ROLLER Endpoints — Confirmed Contracts

**Status: REVIEWED — key fields confirmed. See Notes / surprises sections for items that affect the plan.**

---

## 1. Get Products

**Docs:** https://docs.roller.app/docs/roller-api/7bbac8eaac480-get-products

| Field | Value |
|-------|-------|
| HTTP method | `GET` |
| Path | `https://api.roller.app/data/products` |
| Auth header | `Authorization: Bearer {access_token}` |
| Pagination | `pageNumber` (default 1) + `pageSize` (default 100) query params |

### Response field reference

| Field | Type | Notes |
|-------|------|-------|
| `productId` | string | e.g. `"983361"` |
| `parentProductId` | string | Parent/variant relationship |
| `name` | string | Display name |
| `price` | number or null | **Decimal float** (e.g. `59.99`) — see price format note below |
| `costOfGoods` | number or null | Internal cost, not the sale price |
| `productType` | string | e.g. `"Pass"` — F&B values must be confirmed via live API call |
| `productSubType` | string | e.g. `"Membership"` — F&B values must be confirmed via live API call |
| `reportingCategoryName` | string | Grouping/category label — use as the category display field in the POS UI |
| `productStatus` | string | `Draft` \| `Published` \| `Closed` \| `Archived` |
| `hqProductId` | string | HQ parent product link |
| `hqCode` | string | HQ product code |
| `taxId` | integer or null | Custom tax variation ID |
| `barcodeId` | string | Scannable barcode |
| `parLevel` | integer or null | Min quantity threshold |

### Key fields confirmed

| Field | Confirmed value / notes |
|-------|------------------------|
| `productType` enum values for F&B | **Confirmed.** F&B items use `productType = "AddOn"` (integer `7`). See Product Type Reference table below. |
| `productSubType` enum values for F&B | **Confirmed.** Relevant subtypes: `"Stock"` (integer `8`) for physical food/beverage items with inventory; `"Empty"` (integer `0`) for standard addons; `"OpenProduct"` (integer `3`) for open-price items. See Product Type Reference table. |
| Price field name | `price` |
| Price format | **Decimal float** (e.g. `59.99`), NOT cents. See impact note. |
| Currency field | **Not present** — assumed venue local currency (AUD). No currency field on product records. |
| Image URL field | **Not present** in the documented schema. POS UI should not plan on product images. |
| Category field | `reportingCategoryName` — use this as the display category in the catalogue UI. |

### Notes / surprises

- **F&B filter strategy.** There is no dedicated `productType` value for F&B. The closest mapping is `productType = "AddOn"` with subtypes `Stock`, `Empty`, and `OpenProduct`. However, a venue could place non-F&B addons in the same types. The safest filter for the prototype is `productType == "AddOn"` combined with a `productStatus == "Published"` filter, then use `reportingCategoryName` to group within the catalogue UI. The exact subtype filter (whether to include all AddOn subtypes or only Stock) should be confirmed with the venue before Phase 1b.
- **Price is a decimal float, not cents.** The plan and the `push-charges.md` / `gift-card.md` contracts use integer cents throughout. The POS backend must multiply by 100 and round to convert incoming ROLLER prices to cents for internal storage and calculations, then divide by 100 when calling ROLLER endpoints that expect decimal amounts. Update `push-charges.md` and `gift-card.md` accordingly.
- **No image URL.** The catalogue UI should use a placeholder or category icon instead.
- **`productType` / `productSubType` F&B filter values are unknown.** This is the most critical gap in Phase 0. A live API call is required before Phase 1b can proceed. Use the curl command in `CONTRACTS.md` with the ROLLER API key.
- **`productStatus` filter:** The backend product fetch should filter to `productStatus = "Published"` only, in addition to the F&B type filter.

---

## 2. Search for Bookings

**Docs:** https://docs.roller.app/docs/rest-api/fbb465d1ed24d-search-for-bookings

| Field | Value |
|-------|-------|
| HTTP method | `GET` |
| Path | `https://api.roller.app/bookings` |
| Auth header | `Authorization: Bearer {access_token}` |

### Query parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `keywords` | string | No | Searches Booking Name, Ticket ID, Custom Ticket ID, guest First name, Last name, Email, Phone. **Minimum 3 characters.** Fuzzy match. **Returns at most 10 results.** |
| `date` | string (date) | No | Filter by booking item date |
| `locationIds` | string | No | Comma-separated resource IDs. Requires `date`. |
| `productIds` | string | No | Comma-separated product IDs. Requires `date`. |
| `startTime` | string | No | Session start time in 24hr format. |

### Response shape

```json
{
  "bookings": [
    {
      "bookingReference": "12345678",
      "uniqueId": "ffdae420-a912-4d93-9234-9faca5e6194f",
      "createdDate": "2020-07-31T03:43:40.4085882+00:00",
      "status": "PaidPart",
      "name": "Sports Club Booking",
      "customerId": 77388293,
      "total": 55.5,
      "items": [
        {
          "productId": 12345678,
          "quantity": 26,
          "bookingDate": "2020-07-31",
          "startTime": "14:30"
        }
      ],
      "customerFlags": [
        {
          "type": "Ban",
          "comment": "Guest has repeatedly ignored the rules",
          "expiryDate": "2025-07-14"
        }
      ]
    }
  ]
}
```

### Key fields confirmed

| Field | Confirmed value / notes |
|-------|------------------------|
| Booking ID field names | Two IDs: `bookingReference` (short numeric string, shown to guests) and `uniqueId` (UUID, used in all API calls). |
| Guest name field | `name` — booking title; defaults to booking owner's name. Not necessarily the individual guest name. |
| Guest email field | **Searchable but not returned.** Email is a valid `keywords` search term (docs confirm it searches email), but email is not a field in the response object. The UI can accept email as input but cannot display it in results. |
| Booking date field | No top-level booking date. Date is on each `items[]` entry as `bookingDate`. |
| Booking status field + enum values | `status`: `PendingPayment` \| `NoPaymentRequired` \| `PaidPart` \| `PaidFull`. Note: **different from the detail endpoint status enum**. |
| Total amount field | `total` — decimal float. |
| Search mechanism | All search terms go through the single `keywords` param. Name, email, booking ID, phone all use the same field. |
| Max results returned | **10 results maximum** — documented hard limit. |

### Notes / surprises

- **Single `keywords` param for all search types.** The plan described separate search by name, email, or booking ID as if they were different query params. They are not — all go through `keywords`. The backend search endpoint `GET /api/bookings/search?q=` can pass the query directly to `keywords`. Searching by email works and returns matching bookings, but the email address is not echoed back in the result — the response only includes `name`, `bookingReference`, `status`, and `total`. The search UI should accept email as input but not display an email column in results.
- **Max 10 results.** If a name is common (e.g. "Smith"), only the 10 most recent matching bookings are returned. The POS search UI should note this limitation.
- **Guest email not returned.** The `BookingSummary` DTO in Phase 2 (T2.1) should not include an email field — it's not available without a separate customer API call. Remove the email column from the search results UI, or show it as "N/A" if the customer API is called per-result (expensive — not recommended for prototype).
- **Two booking IDs.** The operator can search by either `bookingReference` (short numeric, what guests see on their confirmation email) or `uniqueId`. Both work via `keywords`. However, all backend API calls (detail, update, payments) must use `uniqueId`. The POS must store `uniqueId` as the authoritative identifier and display `bookingReference` to the operator.
- **`customerFlags`** (Ban, VIP, Medical, Alert, Competency) are returned in search results. The POS UI should surface a "Ban" flag prominently before allowing a tab to be opened.

---

## 3. Get Detail of a Booking

**Docs:** https://docs.roller.app/docs/rest-api/olt8a8nxs75ev-get-detail-of-a-booking

| Field | Value |
|-------|-------|
| HTTP method | `GET` |
| Path | `https://api.roller.app/bookings/{uniqueId}` |
| Path parameter | `uniqueId` (UUID) — **not** `bookingReference` |
| Auth header | `Authorization: Bearer {access_token}` |

### Response field reference

| Field | Type | Notes |
|-------|------|-------|
| `bookingReference` | string | Short numeric ID for display |
| `uniqueId` | string (UUID) | API identifier |
| `externalId` | string | External system reference |
| `createdDate` | string | ISO 8601 |
| `channel` | string | `VenueManager` \| `POS` \| `Online` \| `Forms` \| `SSK` \| `DataImport` \| `Api` \| `Marketplaces` |
| `status` | string | `Draft` \| `NoPaymentRequired` \| `PendingPayment` \| `PartiallyPaid` \| `Paid` \| `Cancelled` \| `Deleted` |
| `name` | string | Booking title |
| `customerId` | integer | Use Customer API to get email/phone |
| `total` | number | Total booking cost (decimal float) |
| `remainder` | number | **Amount still owing** — use this for the "fully pre-paid" guard |
| `fees` | number | Booking fees |
| `discount` | number | Discount applied |
| `items` | object | Booking line items (see below) |
| `items.bookingItemId` | integer | Unique item ID within this booking — used in `idsToRemove` for updates |
| `items.productId` | integer | Child/variation product ID |
| `items.quantity` | integer | Quantity |
| `items.bookingDate` | string | Date item is valid from |
| `items.sessionStartTime` | string | 24hr format |
| `items.sessionEndTime` | string | 24hr format |
| `items.cost` | number | Unit cost (decimal float, per item, not per quantity) |
| `items.discount` | number | Total discount on this item across all quantities |
| `items.tickets` | BookingTicket | Ticket details (ticketId, ticketHolderName, etc.) |
| `deviceId` | integer | POS device ID if applicable |
| `posNotes` | string | Notes from POS |
| `payments` | array | Existing payment records |
| `payments[].bookingPaymentId` | integer | Payment record ID |
| `payments[].transactionId` | string | Transaction ID (gift card number if method is Giftcard) |
| `payments[].paymentMethod` | string | `CreditCard` \| `CreditCardPreAuth` \| `Cash` \| `Eftpos` \| `PayPal` \| `Other` \| `Giftcard` \| `BankTransfer` \| `BPay` \| `Cheque` \| `Prepaid` \| `Points` \| `Wallet` \| `Groupon` \| `Cashless Card` \| `ThirdParty` |
| `payments[].total` | number | Payment amount |
| `payments[].tip` | number | Tip amount |
| `payments[].createdDate` | string | ISO 8601 |

### Key fields confirmed

| Field | Confirmed value / notes |
|-------|------------------------|
| Booking ID field name | `uniqueId` (UUID) for API calls; `bookingReference` for display |
| Booking status enum values | `Draft` \| `NoPaymentRequired` \| `PendingPayment` \| `PartiallyPaid` \| `Paid` \| `Cancelled` \| `Deleted` |
| Line items array field name | `items` (object, not array — may be a single object in the docs example; confirm if it is always an array in the real response) |
| Line item: product ID field | `items.productId` (integer) |
| Line item: product name field | **Not present** — only `productId`. Must cross-reference with product catalogue to display names. |
| Line item: quantity field | `items.quantity` |
| Line item: unit price field + format | `items.cost` — decimal float (e.g. `8.9`) |
| Line item: GST amount field | **Not present** — no GST breakdown at item level. POS must compute GST (÷ 11 for 10% GST inclusive). |
| Line item: line total field | **Not present** — compute as `cost × quantity` |
| Tickets array field name | `items.tickets` (nested within each item) |
| Total amount field | `total` — decimal float, includes fees |
| Remaining balance field | `remainder` — amount still owing. `remainder == 0` means fully paid. |
| Payment status field + enum values | `status` — see table above. Note: different enum values from the search endpoint. |
| Guest name / email fields | `name` (booking title) — email **not present**, requires Customer API call with `customerId` |

### Notes / surprises

- **`items` may be an object, not an array.** The docs example shows `items` as a single object. Confirm in the live API whether it wraps a single item or is always an array. If it is an object when there is one item, the POS backend must normalise this to an array.
- **No product name on items.** `items.productId` is an integer but the product name is not included. When importing a booking, the POS must look up product names from the catalogue by `productId`. This is a required cross-reference — the `FnbProduct` list returned by `/data/products` uses string `productId`, while booking items use integer `productId`. Ensure the comparison is type-safe.
- **No GST breakdown at item level.** The POS must compute GST: `gst = cost - (cost / 1.1)`, rounded to 2 decimal places.
- **`remainder` is the "fully pre-paid" guard.** In Phase 2 (T2.2), check `remainder == 0` (or `remainder < 0.01` to guard against floating-point) to determine if a tab is needed.
- **Status enum mismatch with search endpoint.** Search returns `PaidPart`; detail returns `PartiallyPaid`. The POS must handle both and not assume they are consistent.
- **`customerFlags`** — not present in booking detail (only in search). If the operator searches, sees a Ban flag, and then imports — the flag won't reappear on the detail. Consider storing the flag from the search result on the Tab record.

---

## 4. Update a Booking

**Docs:** https://docs.roller.app/docs/rest-api/v4mzj4t4erwa9-update-a-booking

| Field | Value |
|-------|-------|
| HTTP method | `PUT` |
| Path | `https://api.roller.app/bookings/{uniqueId}` |
| Path parameter | `uniqueId` (UUID) |
| Auth header | `Authorization: Bearer {access_token}` |
| Partial update | The request body only needs to include fields being changed. Omitted fields appear to be unchanged (confirm against live API). |

### Writable fields

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Booking title (≤ 128 chars) |
| `comments` | string | Additional detail (≤ 500 chars) |
| `newItems` | array | Products to add. Each: `productId`, `quantity`, `bookingDate`, `startTime`, `priceOverride`, `packageInclusions`, `tickets` |
| `idsToRemove` | array[integer] | `bookingItemId` values of items to remove |
| `itemsToUpdate` | array | Update existing items: `bookingItemId` (required), `bookingEndDate`, `tickets` |

### Key fields confirmed

| Field | Confirmed value / notes |
|-------|------------------------|
| Time slot / session field | `newItems[].startTime` — writable when adding new items. No direct field to update `sessionStartTime` of an existing item (use `idsToRemove` + `newItems` to reschedule). |
| Guest count field | Not a direct field — guest count is implicit in the number of items/tickets. To change guest count, add or remove items. |
| Capacity field | Not visible as a directly writable field. |
| Notes / special instructions | `comments` |
| Payment amount — is it writable? | **Payment amount is not a field in the PUT request body.** Payments are managed via the `/payments` endpoint. This aligns with the payment lock design — the lock only needs to block `newItems` and `idsToRemove` operations (which indirectly change the total), not a payment amount field. |
| Payment method — is it writable? | **Not a field here.** Payments are separate. |
| Always read-only | `bookingReference`, `uniqueId`, `createdDate`, `status`, `total`, `remainder` — none of these appear in the writable schema. |

### Notes / surprises

- **Payment lock design impact.** There is no direct "payment amount" field to block. The payment lock must block `newItems` and `idsToRemove` operations (which change the booking composition and therefore the total). Operations on `name`, `comments`, and `itemsToUpdate` (ticket names, end dates) should remain unblocked. This is more nuanced than a simple "payment fields locked" — the ROLLER backend team building the lock endpoint must target the `newItems` and `idsToRemove` keys specifically.
- **No direct session rescheduling.** Changing a session time requires removing the item and re-adding it. This is important for the Phase 3 acceptance gate: "non-payment edits still work on a locked booking" — we need to verify whether a remove+re-add operation is treated as a payment-affecting change (changes total) or a non-payment edit. Clarify with ROLLER team.
- **`priceOverride`** on new items could affect the booking total. This field should also be blocked by the payment lock when the tab is open.

---

## 5. Add Transaction Record

**Docs:** https://docs.roller.app/docs/rest-api/a86n5aasxe98r-add-transaction-record

| Field | Value |
|-------|-------|
| HTTP method | `POST` |
| Path | `https://api.roller.app/bookings/{uniqueId}/payments` |
| Path parameter | `uniqueId` (UUID) |
| Auth header | `Authorization: Bearer {access_token}` |
| Idempotency | The caller-provided `id` field must be unique — duplicate `id` values are rejected. This is the de-facto idempotency mechanism. |

### Request body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Caller-provided unique identifier. Acts as idempotency key — reuse the same `id` to safely retry. |
| `paymentType` | string | Yes | `CreditCard` \| `CreditCardPreAuth` \| `Cash` \| `Cheque` \| `BankTransfer` \| `Other` — see `payments.md` for the `Giftcard` addition |
| `amount` | number | Yes | Decimal float. Negative for refunds. Venue local currency. |
| `creditCardFees` | number | No | Fees to add |
| `transactionDate` | string | No | ISO 8601 UTC. Defaults to now if omitted. |

### Response

```json
{ "uniqueId": "ffdae420-a912-4d93-9234-9faca5e6194f" }
```

Returns the booking's `uniqueId` — not a payment record ID.


## 6. Get Guest detail
get https://api.roller.app/guests/{guestId}

guestId
string
required
ID of a guest record (Formerly customerId, which is equivalent)
Example:
32444

### Key fields confirmed
Record of guest details

firstName
string
required
First name of the customer

<= 32 characters
Example:
John
lastName
string
required
Last name of the customer

<= 32 characters
Example:
Smith
email
string or null
required
Email address of the customer (Required only if phone is not provided).

Provided value must be a valid email pattern.
<= 256 characters
Example:
johnsmith@noemail.com
phone
string or null
required
Phone number of the customer (Required only if email is not provided).

<= 20 characters
Example:
12345678
dateOfBirth
string or null
Date of Birth of the guest in YYYY-MM-DD format.

Date of Birth must not be before 1900 or in the future.
Example:
2001-03-26
gender
any
Gender of the guest

Allowed values:
Male
Female
Other
PreferNotToSay
acceptMarketing
boolean
When true the guest has accepted to receive email marketing content

Example:
false
acceptMarketingSms
boolean
When true the guest has accepted to receive SMS marketing content

Example:
false
taxIdentificationNumber
string
Tax identification number that may be used for maintaining fiscal compliance

<= 64 characters
address
object or null
street
string
Street number and name of the customer

<= 256 characters
Example:
123 Fake Street
suburb
string
Suburb of the customer

<= 64 characters
Example:
Orange County
city
string
City of the customer

<= 64 characters
Example:
Los Angeles
state
string
State / province of the customer

<= 64 characters
Example:
CA
postcode
string
Post code / ZIP code of the customer

<= 12 characters
Example:
27385
country
string
Country of the customer

<= 64 characters
Example:
United States
flags
array[CustomerFlag]
Flag information that may be associated with the guest record.Show all...

type
any
Type of flag associated with the guest

Allowed values:
Ban
VIP
Medical
Alert
Competency
comment
string
Further information regarding the flag

Example:
Guest has repeatedly ignored the rules
expiryDate
string<date> or null
The date the flag expires and is no longer relevant for the guest

Example:
2025-07-14

---

## Outstanding TODOs (must complete before Phase 1b)

| Item | Owner | Blocker for | Status |
|------|-------|-------------|--------|
| Confirm `productType` / `productSubType` F&B enum values | POS team | Phase 1b (catalogue filter) | **Resolved** — filter on `productType = "AddOn"`, exclude Donation and ExternalGiftCard subtypes. Confirm OpenProduct inclusion with venue. |
| Confirm auth header name and format | POS team | Phase 1a (API client) | **Resolved** — OAuth 2.0 client credentials. `POST /token` → `Authorization: Bearer {token}`. |
| Confirm whether `items` in booking detail is object or array | POS team | Phase 2 (booking import) | Open — verify against live API |
| Update `/payments` endpoint to accept `Giftcard` paymentType | ROLLER team | Phase 5 (gift card settlement) | **Resolved** — `Giftcard` cannot be posted today. ROLLER team to add it to the `paymentType` enum. New gift card balance/deduction endpoints also required (see `gift-card.md`). |
| Confirm payment lock should block `newItems`/`idsToRemove`/`priceOverride` specifically | ROLLER team | Phase 3 (lock design) | Open |

---

## Product Type Reference

Full enum mapping for `productType` and `productSubType` as returned by the ROLLER API.

| ROLLER Product | `productType` | `productSubType` | F&B? |
|----------------|--------------|-----------------|------|
| Standard pass | `6` / `"Pass"` | `0` / `"Empty"` | No |
| Session pass | `6` / `"Pass"` | `7` / `"Session"` | No |
| Recurring pass | `6` / `"Pass"` | `4` / `"RecurringSessions"` | No |
| Package | `10` / `"Package"` | `0` / `"Empty"` | No |
| Party package | `13` / `"PartyPackage"` | `0` / `"Empty"` | No |
| Gift card | `9` / `"GiftCard"` | `0` / `"Empty"` | No |
| Membership | `6` / `"Pass"` | `5` / `"Membership"` | No |
| **Stock** | **`7` / `"AddOn"`** | **`8` / `"Stock"`** | **Primary F&B type** |
| **Addon (Standard)** | **`7` / `"AddOn"`** | **`0` / `"Empty"`** | **Possible F&B** |
| Addon (Donation) | `7` / `"AddOn"` | `1` / `"Donation"` | No |
| **Addon (Open Product)** | **`7` / `"AddOn"`** | **`3` / `"OpenProduct"`** | **Possible F&B** |
| Addon (External gift card) | `7` / `"AddOn"` | `6` / `"ExternalGiftCard"` | No |
| Wallet | `11` / `"Wallet"` | `0` / `"Empty"` | No |
| Cashless card | `12` / `"CashlessCard"` | `0` / `"Empty"` | No |

**POS backend filter (Phase 1b):** Filter to `productType == "AddOn"` (or integer `7`) and `productStatus == "Published"`. Exclude `productSubType` values `"Donation"` (`1`) and `"ExternalGiftCard"` (`6`). Use `reportingCategoryName` for grouping within the catalogue UI. Confirm with the venue whether `"OpenProduct"` addons should appear in the POS catalogue.

---

## Authentication

ROLLER uses **OAuth 2.0 client credentials flow**. The POS backend must obtain a bearer token before making any API call.

### Token endpoint

```
POST https://api.roller.app/token
Content-Type: application/json

{ "client_id": "...", "client_secret": "..." }
```

### Token response

```json
{
  "access_token": "cd5c24313225bb9ea046a2ef0f0dbb9f...",
  "token_type": "Bearer",
  "expires_in": 86400
}
```

### Using the token

All API requests must include:

```
Authorization: Bearer {access_token}
Accept: application/json
```

### Token lifecycle — implementation rules for `RollerApiClient`

| Rule | Detail |
|------|--------|
| Cache the token | Store in memory (`IMemoryCache`). Do not request a new token per API call. |
| Expiry | `expires_in` is currently 86400 seconds (24 hours) but **may change** — always read it from the response, never hardcode. Evict from cache at `expires_in - 60` seconds to refresh proactively. |
| On 401 response | Discard the cached token, fetch a new one, and retry the original request **once**. |
| On 429 response | ROLLER explicitly warns against multiple servers each fetching their own tokens. For the prototype (single server), in-memory cache is sufficient. |
| Credentials source | Read `ROLLER_CLIENT_ID` and `ROLLER_CLIENT_SECRET` from environment variables / `.env` — never hardcode.

## Sign-off

| Reviewer | Date | Signature |
|----------|------|-----------|
| | | |

_All outstanding TODOs must be resolved and this table signed before Phase 1b and Phase 2 begin._


