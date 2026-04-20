# Payment Lock / Unlock — New ROLLER Endpoint Contract

**Status:** DRAFT — pending ROLLER team review and approval  
**Phase:** Designed in Phase 0 (T0.4) · Built by ROLLER team in Phase 3 (T3.1)  
**POS stub:** `PosApi.Stubs` project (T3.2) — used until real endpoint is available

---

## Overview

These two endpoints allow the POS to acquire and release a payment-level lock on a ROLLER booking. The lock prevents payment fields from being modified externally while a tab is open, while intentionally leaving non-payment fields (time slot, guest count, notes, capacity) editable.

**Key design constraints:**
- Non-payment fields must remain writable while a lock is active.
- The refund path must remain accessible regardless of lock state (confirm exact scope with ROLLER product team).
- No server-side TTL in the prototype — flagged as a production gap (see OQ6 resolution in IMPLEMENTATION_PLAN.md).
- A booking can only hold one lock at a time. Concurrent lock attempts from different POS sessions return 409.

---

## Endpoint 1 — Acquire Lock

### Request

```
POST /api/v1/bookings/{bookingId}/payment-lock
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
  "lockedBySystem": "pos",
  "lockedByReference": "{tabId}",
  "reason": "tab_opened"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `lockedBySystem` | string | Yes | Identifier of the system acquiring the lock. Fixed value `"pos"` for this prototype. |
| `lockedByReference` | string | Yes | The POS `tabId` (UUID). Stored by ROLLER for traceability. |
| `reason` | enum | Yes | `"tab_opened"` — only valid value in prototype scope. |

### Responses

**200 OK — lock acquired**

```json
{
  "lockId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "bookingId": "string",
  "lockedAt": "2026-04-20T03:00:00Z",
  "lockedBySystem": "pos",
  "lockedByReference": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "status": "locked"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `lockId` | UUID | Must be stored by the POS on the `Tab` record. Required to release the lock. |
| `bookingId` | string | Echo of the path parameter. |
| `lockedAt` | ISO 8601 | Server-side timestamp. |
| `lockedBySystem` | string | Echo of request field. |
| `lockedByReference` | string | Echo of request field (`tabId`). |
| `status` | enum | `"locked"` |

**409 Conflict — booking already locked by another session**

```json
{
  "error": "booking_already_locked",
  "lockedBySystem": "pos",
  "lockedByReference": "other-tab-id",
  "lockedAt": "2026-04-20T02:45:00Z"
}
```

**404 Not Found — booking does not exist**

```json
{
  "error": "booking_not_found"
}
```

---

## Endpoint 2 — Release Lock

### Request

```
DELETE /api/v1/bookings/{bookingId}/payment-lock
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
  "lockId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "reason": "tab_settled"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `lockId` | UUID | Yes | The `lockId` returned when the lock was acquired. Must match the active lock — prevents accidental release by a different POS session. |
| `reason` | enum | Yes | One of: `"tab_settled"` \| `"manual_override"` \| `"system_crash_recovery"` |

### Responses

**200 OK — lock released**

```json
{
  "bookingId": "string",
  "unlockedAt": "2026-04-20T04:15:00Z",
  "status": "unlocked"
}
```

**404 Not Found — no active lock on this booking**

```json
{
  "error": "lock_not_found"
}
```

**403 Forbidden — `lockId` does not match the active lock**

```json
{
  "error": "lock_id_mismatch"
}
```

---

## POS Integration Points

| Event | Action |
|-------|--------|
| Booking imported (`POST /api/tabs/from-booking`) | Call lock endpoint immediately. If 409 or non-200, abort tab creation and surface error to operator. |
| Tab settled successfully | Call unlock with `reason: "tab_settled"`. Non-blocking on failure — record `unlockFailed` flag on tab. |
| Walk-out charge processed at till close | Call unlock with `reason: "tab_settled"`. Non-blocking on failure. |
| Admin force-release (`DELETE /api/admin/tabs/{tabId}/lock`) | Call unlock with `reason: "manual_override"`. |
| Crash recovery / stuck lock background monitor | Call unlock with `reason: "system_crash_recovery"` after operator confirms via admin UI. |

---

## Open Questions for ROLLER Team

1. **Refund access while locked:** Does the lock block refund initiation? If so, what is the exact refund flow when a guest disputes a charge on a locked booking?
2. **Non-payment fields:** Provide the exhaustive list of fields that must remain writable while `status = "locked"`. The POS needs to verify this in the Phase 3 approval gate.
3. **Partial lock:** Is there a future need to lock only specific line items rather than the whole booking? Not required for prototype — flagged for production scoping.
4. **TTL for production:** What should the TTL policy be? The prototype has no TTL; production must define one.

---

## Sign-off

| Reviewer | Role | Date | Approved |
|----------|------|------|----------|
| | ROLLER Backend | | |
| | POS Lead | | |
