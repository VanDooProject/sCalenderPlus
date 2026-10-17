import type { LegalPage } from './routes'
import type { SiteConfig } from './siteConfig'

/** The build variable that replaces a template with the operator's own page. */
export const legalVariables: Record<LegalPage, string> = {
  imprint: 'VITE_IMPRINT_URL',
  privacy: 'VITE_PRIVACY_URL',
  terms: 'VITE_TERMS_URL',
  dpa: 'VITE_DPA_URL',
}

/** The operator's own page, if configured (else the template page of this site). */
export function legalUrl(site: SiteConfig, doc: LegalPage): string | null {
  switch (doc) {
    case 'imprint':
      return site.imprintUrl
    case 'privacy':
      return site.privacyUrl
    case 'terms':
      return site.termsUrl
    case 'dpa':
      return null
  }
}

/** Placeholder DPA shipped with the landing (public/), replaced by `VITE_DPA_URL`. */
export const dpaPlaceholderPath = '/downloads/dpa-template.pdf'
