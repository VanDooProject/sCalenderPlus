# Privacy Levels and Cover Titles

Each calendar has a **privacy level** that decides what the server can read. Only level C is built; B and A are specified so that today's data model and API don't block them. Cover titles (§5) are built now for level C.

## 1. Levels

| Level | Key | What is encrypted end-to-end | Status |
|---|---|---|---|
| **C** | `standard` | Nothing beyond transport (TLS) and disk encryption; the server sees everything. | built (default) |
| **B** | `content_encrypted` | Title, description, location. Times, recurrence, ACL and the cover title (§5) stay in clear. | later |
| **A** | `end_to_end` | Everything incl. times and recurrence; only ACL metadata needed for routing stays clear. | later, maybe |

The level is chosen at calendar creation and can **only be raised** (C → B → A), never lowered, and never silently: raising re-encrypts existing events client-side; a lower level requires creating a new calendar and moving events explicitly (which the client decrypts and re-encrypts).

## 2. Comparison

| Aspect | C `standard` | B `content_encrypted` | A `end_to_end` |
|---|---|---|---|
| Server sees | everything | times, recurrence, ACL, cover title; ciphertext for content | ciphertext; ACL and coarse metadata (calendar, size, update time) |
| iCal feeds / CalDAV | full projection | busy-only (times + cover title); full content impossible (feed clients have no keys) | none |
| Google / Microsoft sync ([sync.md](sync.md)) | full or busy-only | busy-only with cover title | none |
| Free/busy views | ✓ | ✓ | only inside our clients (computed client-side) |
| Reminders | email + push with title | email + push with generic text ("Event at 18:00") | local notifications in our clients only |
| Recurrence expansion | server | server | client only (no server-side expansion) |
| Search | server full-text | client-side over decrypted events | client-side only |
| LLM import into the calendar | ✓ | imported content is public; the worker encrypts it to members' public keys | ✗ |
| Lost keys | n/a | content lost (times survive) | everything lost |
| Clients | all | all for busy data; our clients for content | our clients only |

## 3. Mapping to permission levels

The permission engine ([permissions.md](permissions.md)) stays the authority for *who may see what*; keys enforce it cryptographically on top.

| Effective level | Key material (B and A) |
|---|---|
| `none` | No key, event not delivered. |
| `free_busy` | **Never gets a key.** Sees times (B) and the cover title. In A, `free_busy` viewers see only what the client of an authorised user publishes as busy blocks. |
| `read`, `edit`, `manage` | The event key is wrapped for the user's public key. |

- Each event has a random symmetric **event key**; content is encrypted with it. Each new version of an event uses the current key.
- **Revoke** (override or grant removed, member removed): the next editor's client **re-keys future versions** (new event key, wrapped only for remaining readers). Content the revoked user has already seen **cannot be recalled** — same honesty as for feeds ([ical-caldav.md §1.4](ical-caldav.md#14-caching-etags-and-performance)).
- Elevation (`free_busy` → `read`) requires a client holding the key to wrap it for the new reader; the server queues a "needs key" task that the next online authorised client completes. Until then the reader sees the event as `free_busy`.

## 4. Key management

- **Per-user key pair** (X25519 + Ed25519), generated on the first device. The private key never leaves devices unencrypted.
- **Device pairing** by QR code from an existing device; users compare **safety numbers** (fingerprints) of each other's public keys to detect a malicious server substituting keys.
- **Recovery key** shown once at setup (printable); without it and without a paired device, level A data is lost and level B content is lost.
- Group calendars: keys are wrapped per member, not per group, so group membership changes follow the revoke rule above.
- Libraries: audited primitives only (e.g. libsodium in WebAssembly); no custom crypto.

## 5. Cover titles

Built now for level C; kept clear in level B so busy-only consumers can render it.

| Rule | Value |
|---|---|
| Field | `coverTitle` on events, nullable, trimmed, max 200 chars, same validation as `title` |
| Who sets it | anyone who may edit the event (`Le ≥ edit`) |
| `free_busy` viewers | see `coverTitle` (if set) instead of the generic "Busy"; still never description, location or the real title |
| `read` and above | see the real `title` **and** `coverTitle`, so editors know what outsiders see |

Status: **built** in the API, the web app (event dialog, busy rendering) and the iCal feeds (M4): `free_busy` VEVENTs get `SUMMARY:<coverTitle>` instead of the busy label, also per occurrence ([ical-caldav.md §1.3](ical-caldav.md#13-permission-projection-and-access-labels)); the same projection applies to CalDAV (M8) and Google/Microsoft sync.

## 6. Rollout recommendation

1. **C now**, with cover titles — covers most discretion needs without key management.
2. **B as the poly default** once built ([editions.md](../product/editions.md)); optional in standard.
3. **A last, maybe** — it removes feeds, sync, server reminders and search, i.e. most of what makes the product useful in native apps. Build only on proven demand.

## 7. Comparables

Phrased cautiously; check current vendor documentation before using in marketing.

- **Proton Calendar** encrypts event content but, per its documentation, not the event times — conceptually close to level B.
- **Tuta (Tutanota)** encrypts nearly all calendar data incl. times — conceptually close to level A, with the same trade-off of working only in its own clients.
