# Subscribing in Calendar Apps

How people add their personal iCal feeds ([ical-caldav.md §1](../architecture/ical-caldav.md)) to Apple Calendar, Google Calendar, Outlook and Thunderbird, and what each app does about refreshing. The web app shows the same guide at `/help/calendar-apps` (`frontend/app/src/views/help/CalendarAppsView.vue`, en/de in the locale files under `help.calendarApps`); the feed UI links there. Keep both in step: change this page and the messages together.

Refresh behaviour comes from [sync.md](../architecture/sync.md): feeds are polled by the calendar app, we only send hints (`REFRESH-INTERVAL`, `X-PUBLISHED-TTL`, both one hour).

## Before you start

- **Each feed link is personal.** It shows exactly what you may see: events you may only see as busy stay busy (with their cover title, if set), hidden events are missing. A `webcal://` link opens the calendar app directly on devices that have one.
- **Treat the link like a password.** Anyone who has it sees what you see. If it leaks, replace it with a new one; the old link stops working (`404`).
- **Subscriptions are read-only.** Changes are made in the app; calendar apps pick them up at their next refresh.

## Apple Calendar (Mac, iPhone, iPad)

- **Mac:** Calendar → File → New Calendar Subscription, paste the link, Subscribe. Choose iCloud as the location to see it on the other Apple devices too.
- **iPhone, iPad:** tap a `webcal://` link, or Settings → Calendar (iOS 18: Settings → Apps → Calendar) → Calendar Accounts → Add Account → Other → Add Subscribed Calendar.
- **Refresh:** as set by the user (Mac: Auto-refresh in the calendar's info, every 5 or 15 minutes is fine; iOS: Fetch New Data).

## Google Calendar

- **Web only:** calendar.google.com → next to "Other calendars" + → From URL, paste the link, Add calendar. Replace a leading `webcal://` with `https://`.
- **Phones:** the Google Calendar app can't add a calendar by link, but calendars added on the web appear there (Android: turn on Sync for the calendar in the app's settings).
- **Refresh:** Google decides, usually every few hours, sometimes only once a day; it ignores our refresh hints. Neither the user nor we can change it. For faster updates in Google Calendar, API write-through is planned ([sync.md §3](../architecture/sync.md#3-google-calendar-api-and-microsoft-graph-connectors)).

## Outlook

- **Outlook on the web, Outlook.com, new Outlook for Windows:** Calendar → Add calendar → Subscribe from web, paste the link, name it, Import.
- **Classic Outlook for Windows:** Calendar → Add Calendar → From Internet, paste the link, confirm.
- **Phones:** calendars subscribed on the web also appear in Outlook for iOS and Android.
- **Refresh:** Outlook decides, usually every few hours; a change can take up to a day.

## Thunderbird

- Calendar → New Calendar → On the Network, paste the link as the location, finish the wizard; keep it read-only.
- **Refresh:** as set by the user under "Refresh calendar" in the calendar's properties (15 or 30 minutes is plenty).

## Good to know

- Changes reach a calendar app only when it refreshes; our own web app is always current.
- Removing someone's access stops future updates, but copies a calendar app already downloaded stay there until its next refresh. Nobody can recall them sooner ([sync.md §4](../architecture/sync.md#4-what-we-control)).
- Busy-only events appear as "Busy" (or with their cover title) in calendar apps too; hidden events don't appear.
- Feeds are rate-limited per link (60 requests per hour); refreshing every 15 minutes is enough.
