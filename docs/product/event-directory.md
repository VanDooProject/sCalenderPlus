# Event Directory (poly edition)

A poly-only module ([editions.md](editions.md)): **curated public community event calendars** by region and category (e.g. "Berlin · socials", "Vienna · workshops") that users subscribe to or overlay on their own calendars. *Spec only; later.* Built as a poly-only backend assembly, so it never ships in standard builds ([editions.md §2.3](editions.md#23-images)).

## 1. Content sources

| Source | Path into the directory |
|---|---|
| LLM import from organiser websites | Reuses the import pipeline ([llm-import.md](../architecture/llm-import.md)) with the platform as source owner; every candidate goes through the **review queue** ([llm-import.md §7](../architecture/llm-import.md#7-review-queue-vs-auto-publish)), never auto-published. |
| Organiser submissions | Verified organisers create events directly in calendars they own; unverified users submit into the moderation queue. |
| ICS subscription | Organisers with an existing calendar connect it (same dedupe rules). |

## 2. Organisers, verification and claiming

| Concept | Rule |
|---|---|
| Organiser account | A normal user account with a verified organiser profile (name, region, website/social links). |
| Verification | Domain (DNS TXT or file on the organiser's website), social account proof, or manual check by a moderator. Verified status is shown on events and can be revoked. |
| Ownership | Verified organisers **own** their directory calendars and publish without moderation (subject to reports). |
| Imported events | Belong to the platform until **claimed**. A verified organiser whose domain matches the source claims them; claimed events and the source move to the organiser's calendar, history kept. |
| Edits to platform-owned events | Suggested by anyone, applied after moderation. |

## 3. Moderation

| Element | Rule |
|---|---|
| Moderator role | A **platform role** (`moderator`, granted by admins), separate from group roles; it never grants access to private calendars. |
| Moderation queue | Submissions, suggested edits, claims and manual verification requests; the same review UI as the import queue (diff view, bulk accept/reject, edit-then-accept). |
| Reports | Any viewer can report an event or organiser (reasons: spam, wrong data, unsafe, explicit content outside the adult add-on, impersonation); reuses the abuse reporting of share links and widgets ([features.md §10](features.md#10-privacy-compliance-operations)). |
| Takedown | Moderators hide events immediately (soft); organisers are notified and may appeal. |
| Bans | Per organiser or user: submission ban, verification revoked, account suspension; time-limited or permanent. |
| Audit | Every moderation action is an audit event (actor, reason, before/after); retention independent of plan. |

## 4. Going / interested

- Users mark directory events **going** or **interested**; the event is copied (linked) into their own calendar, where per-event permissions apply as usual.
- **Per-event visibility** of the mark, chosen by the user: only me (default), my constellations, or public count only. Names are never public; organisers see counts, not attendees, unless the user opts in.
- Marks are personal data under GDPR Art. 9 in this edition ([editions.md §7.1](editions.md#71-gdpr-art-9)) and are deleted with the account.

## 5. Explicit content

Directory images and adult listings are only visible with the **adult add-on** ([editions.md §7.2](editions.md#72-adult-add-on)); without it, such listings are hidden and their images replaced by a neutral placeholder.
