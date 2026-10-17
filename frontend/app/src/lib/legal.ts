import type { BrandConfig } from '@/config'

/** The legal pages every deployment links (footer of every page, sign-up). */
export const legalDocuments = ['imprint', 'privacy', 'terms'] as const
export type LegalDocument = (typeof legalDocuments)[number]

/** Route names of the built-in pages (`/legal/imprint`, …). */
export const legalRouteName = (doc: LegalDocument) => `legal-${doc}` as const

/** The deployment's own page (`PUBLIC_BRAND_*_URL` in /config.json), if configured. */
export function legalUrl(brand: BrandConfig, doc: LegalDocument): string | null {
  switch (doc) {
    case 'imprint':
      return brand.imprintUrl
    case 'privacy':
      return brand.privacyUrl
    case 'terms':
      return brand.termsUrl
  }
}

/**
 * Sections of the built-in template pages (messages `legal.pages.<doc>.<section>.title|text`):
 * minimal, clearly marked placeholders for the operator, no invented company data.
 */
export const legalSections: Record<LegalDocument, readonly string[]> = {
  imprint: ['provider', 'contact', 'register', 'responsible'],
  privacy: ['controller', 'data', 'cookies', 'emails', 'calendarApps', 'rights', 'operator'],
  terms: ['service', 'account', 'use', 'content', 'plans', 'end', 'operator'],
}
