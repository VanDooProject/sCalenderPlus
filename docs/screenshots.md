# Screenshots

The app in mock mode (no backend), with a fixed date, viewport and locale. Regenerate with `pnpm -C e2e screenshots` (writes `docs/screenshots/`; set `PLAYWRIGHT_CHROMIUM_EXECUTABLE` for a local Chromium, see [`e2e/playwright.screenshots.config.ts`](../e2e/playwright.screenshots.config.ts)).

## Calendar

![Week view with events from several calendars, busy blocks and read-only events](screenshots/calendar-week.png)

The week view overlays all calendars you can see, each in its own colour, so club, family and personal plans sit side by side.

![Month view with timed events, all-day events and busy markers](screenshots/calendar-month.png)

The month view keeps the same per-event permissions: busy-only entries stay anonymous and read-only events carry a lock.

![Event details dialog of a shared event that can only be viewed](screenshots/event-details.png)

Event details say where an event comes from, who created it and exactly what you may do with it, here an event shared with you for viewing only.

![Event edit dialog with title, calendar, all-day toggle, dates, times and the time zone](screenshots/event-edit.png)

Events you may edit open in the same dialog that creates them: dates and times in your zone by default, the event's own time zone under "More options", and every save guarded against concurrent changes.

## Permissions & sharing

![Busy-only blocks next to titled and read-only events in the week grid](screenshots/calendar-busy-blocks.png)

Busy-only blocks show that a colleague is unavailable without revealing title or details, right next to events you may see in full; the lock marks events you can only read.

![Calendar settings: the Sharing card with grants to Eve (Edit) and Max (View)](screenshots/calendar-sharing.png)

Calendar sharing grants individual people or whole groups a level (free/busy, view, add events, edit, manage) on top of the group defaults.

![Access by group role with Member lowered to Free/busy and the "Also revoke individual event shares" option](screenshots/calendar-role-defaults.png)

Group roles get default access per calendar; lowering a role offers to also revoke event shares given personally to those members, so nothing leaks through older per-event grants.

## Groups

![FC Lions group: members list with Owner, Admin, Member and Viewer roles](screenshots/group-members.png)

Groups have owners, admins, members and viewers; the role decides what each person gets on the group's calendars.

![Group invites: invite by email, invite link with role, expiry and usage limit, pending invitations](screenshots/group-invites.png)

Invite people by email or with a link that expires and has a usage limit; links never grant more than the member role.

## Account

![Sign-in page](screenshots/login.png)

A clean sign-in with password, "keep me signed in" and self-service password reset.

![Security settings: two-factor setup with QR code and manual key](screenshots/two-factor-setup.png)

Two-factor authentication with any authenticator app, with recovery codes after setup.

## Mobile

<img src="screenshots/mobile-agenda.png" alt="Agenda view on a phone with busy entries and a read-only event" width="390">

On a phone the calendar opens in the agenda view, with the same busy blocks and lock icons as on the desktop.

## Dark mode

![Week view in dark mode](screenshots/calendar-week-dark.png)

Light, dark or follow the system, remembered per device.

![Profile settings in dark mode](screenshots/profile-dark.png)

Profile settings: display name, language (also used for emails), first day of the week and time zone.

## Localisation

![Week view in German](screenshots/calendar-week-de.png)

The whole app is available in English and German, including dates and times (24-hour clock in German).
