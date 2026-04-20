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
POST /api/v1/bookings/{uniqueId}/payment-lock
Authorization: Bearer {apiKey}
Content-Type: application/json
```

**Path parameters**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `uniqueId` | string | Yes | The ROLLER booking identifier |

**Request body**

```json
{
}
```

### Responses

**200 OK — lock acquired**

```json
{
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
DELETE /api/v1/bookings/{uniqueId}/payment-lock
Authorization: Bearer {apiKey}
Content-Type: application/json
```

**Path parameters**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `uniqueId` | string | Yes | The ROLLER booking identifier |

**Request body**

### Responses

**200 OK — lock released**

```json
{
}
```

**404 Not Found — no active lock on this booking**

```json
{
  "error": "lock_not_found"
}
```

---

## POS Integration Points

| Event | Action |
|-------|--------|
| Booking imported (`POST /api/tabs/from-booking`) | Call lock endpoint immediately. If 409 or non-200, abort tab creation and surface error to operator. |
| Admin force-release (`DELETE /api/admin/tabs/{tabId}/lock`) | Call unlock with `reason: "manual_override"`. |

---

## Open Questions for ROLLER Team

---

## Sign-off

| Reviewer | Role | Date | Approved |
|----------|------|------|----------|
| | ROLLER Backend | | |
| | POS Lead | | |
