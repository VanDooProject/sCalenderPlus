/** Neutral color of "Shared with me" events (their calendar is not the user's to color). */
export const sharedColor = '#64748b'

/** Color of a calendar without one. */
export const fallbackColor = '#4f46e5'

/** Colors offered for calendars and the personal overlay (names are `calendar.colors.*` keys). */
export const calendarPalette = [
  { name: 'indigo', value: '#4f46e5' },
  { name: 'blue', value: '#0284c7' },
  { name: 'teal', value: '#0d9488' },
  { name: 'green', value: '#16a34a' },
  { name: 'amber', value: '#d97706' },
  { name: 'orange', value: '#ea580c' },
  { name: 'red', value: '#dc2626' },
  { name: 'pink', value: '#db2777' },
  { name: 'purple', value: '#9333ea' },
  { name: 'slate', value: '#64748b' },
] as const
