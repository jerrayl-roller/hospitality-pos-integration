# ROLLER Webhooks — Confirmed Contract

**Status:** INCOMPLETE — requires manual review of ROLLER webhook documentation  
**Phase:** T0.3 (discovery) · T4.1–T4.4 (implementation)

---

## What to Confirm (T0.3 Checklist)

Open the ROLLER webhook documentation and confirm each item below before Phase 4 begins.

| Item | Status | Confirmed value |
|------|--------|-----------------|
| Event name for booking amendments | TODO | Assumed `booking_updated` — confirm exact string |
| Payload origin validation mechanism | TODO | HMAC-SHA256 / shared secret header / IP allowlist? |
| Signature header name | TODO | e.g. `X-Roller-Signature`, `X-Hub-Signature-256` |
| Signature algorithm | TODO | e.g. `HMAC-SHA256(rawBody, sharedSecret)` |
| Payload shape: full object or delta? | TODO | Does the payload contain the full booking, or just the booking ID + changed fields? |
| Webhook registration mechanism | TODO | API call or ROLLER portal UI? Provide the registration endpoint / UI path. |
| Retry behaviour | TODO | How many retries does ROLLER attempt on non-2xx? What is the retry schedule? |
| Expected response from POS endpoint | TODO | 200 with empty body? Specific JSON ack? |
| Shared secret delivery mechanism | TODO | How is the secret provided to the POS? Environment variable? ROLLER portal? |

---

## Webhook Endpoint (POS side)

Once the above is confirmed, the POS will expose:

```
POST /api/webhooks/roller
```

### Validation (fill in after T0.3)

```csharp
// TODO: replace with confirmed mechanism

// If HMAC-SHA256:
var rawBody = await request.Body.ReadAsStringAsync();
var computedSignature = HMACSHA256(rawBody, config["Roller:WebhookSecret"]);
var receivedSignature = request.Headers["TODO-HEADER-NAME"];
if (!CryptographicOperations.FixedTimeEquals(computedSignature, receivedSignature))
    return Unauthorized();

// If shared secret header:
var receivedSecret = request.Headers["TODO-HEADER-NAME"];
if (receivedSecret != config["Roller:WebhookSecret"])
    return Unauthorized();
```

### Expected payload shape (assumed — confirm from docs)

```jsonc
// Assumption A: full booking object in payload
{
  "event": "booking_updated",
  "occurredAt": "2026-04-20T03:00:00Z",
  "data": {
    "bookingId": "string",
    // ... full booking object (same shape as GET /bookings/{id} response)
  }
}

// Assumption B: delta/notification only — POS must re-fetch
{
  "event": "booking_updated",
  "occurredAt": "2026-04-20T03:00:00Z",
  "data": {
    "bookingId": "string"
    // No booking details — POS calls GET /bookings/{id} to get current state
  }
}
```

**The webhook processor (T4.2) is designed around Assumption B** (payload contains `bookingId` only; the POS always re-fetches the full booking from ROLLER). This is the safer design — it avoids stale payloads and works regardless of whether ROLLER sends full or partial data.

If Assumption A is confirmed (full booking in payload), the re-fetch can be skipped as an optimisation, but this should be a deliberate code change after confirmation — not an assumption baked into the initial build.

### Processing flow

```
POST /api/webhooks/roller
    │
    ├── Validate signature → 401 if invalid
    ├── Return 200 immediately (do not block on processing)
    └── Enqueue to Channel<WebhookPayload>
            │
            └── BackgroundService reads queue
                    ├── Confirm event type is booking_updated (ignore others)
                    ├── Check for open tab matching bookingId
                    │       └── No tab → log and discard
                    ├── Fetch fresh booking from GET /bookings/{id}
                    ├── Diff against tab.importedItemsJson
                    ├── If conflicts → set tab.HasPendingConflict, append to AuditLogJson
                    ├── Merge non-conflicting changes into tab
                    └── SignalR push → tab-updated event to connected clients
```

### Response

The POS endpoint returns `200 OK` with an empty body immediately after validation. All processing happens asynchronously. This prevents ROLLER's webhook delivery from timing out on slow processing.

---

## SignalR Events (Frontend)

Events emitted to clients via the `TabHub` (`/hubs/tab`) after webhook processing:

**No conflict:**
```json
{
  "tabId": "uuid",
  "eventType": "booking_updated",
  "conflictItems": [],
  "updatedFields": ["guestCount", "sessionTime"]
}
```

**Conflict detected (OQ5):**
```json
{
  "tabId": "uuid",
  "eventType": "conflict_detected",
  "conflictItems": [
    {
      "productId": "prod_abc123",
      "productName": "Craft Beer",
      "conflictType": "removed_from_booking",
      "tabQuantity": 2
    }
  ],
  "updatedFields": []
}
```

`conflictType` values: `"removed_from_booking"` | `"quantity_changed"` | `"price_changed"`

---

## ngrok Setup (Local Development)

ROLLER cannot reach `localhost`. For local development, ngrok creates a public tunnel.

```bash
# Install ngrok (one-time)
# https://ngrok.com/download

# Start tunnel (run this before registering the webhook with ROLLER)
ngrok http 5000

# Copy the HTTPS forwarding URL, e.g.:
# https://a1b2c3d4.ngrok-free.app

# Register webhook with ROLLER using that URL:
# https://a1b2c3d4.ngrok-free.app/api/webhooks/roller
```

**Important:** The ngrok URL changes every time ngrok restarts (on the free plan). You must re-register the webhook URL with ROLLER after each restart. Document the re-registration steps in `DEVELOPMENT_SETUP.md`.

For a stable URL across restarts, use a paid ngrok plan with a reserved domain, or use `ngrok http --domain=your-reserved-domain.ngrok-free.app 5000`.

---

## Sign-off

| Item | Confirmed by | Date |
|------|-------------|------|
| Event name (`booking_updated`) | | |
| Signature mechanism | | |
| Payload shape (full vs. delta) | | |
| Registration mechanism | | |
| Retry behaviour | | |
