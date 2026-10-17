/**
 * Build-time settings of the landing (docs/deployment/coolify.md §13). The site is prerendered static
 * HTML whose canonical URLs, Open Graph tags and sitemap need absolute URLs, so these are resolved
 * when building (Vite env / Docker build args), not at container start: a white-label deployment
 * builds its own landing image. Resolved in vite.config.ts and injected as `__SITE__`.
 */
export interface SiteConfig {
  edition: string
  /** Origin of the landing itself (canonical URLs, sitemap, OG images), without trailing slash. */
  siteUrl: string
  /** Origin of the web app (sign-in and sign-up links). */
  appUrl: string
  brandName: string
  /** OKLCH hue (0–360) of the brand palette (`--scal-brand-hue`). */
  brandHue: number
  /** Contact for support, the importer bot and legal requests; none = placeholder for the operator. */
  supportEmail: string | null
  /** The operator's own legal pages; none = the built-in templates (`/legal/*`). */
  imprintUrl: string | null
  privacyUrl: string | null
  termsUrl: string | null
  /** The operator's data processing agreement; none = the bundled placeholder file. */
  dpaUrl: string | null
  /** User agent of the importer (docs/architecture/llm-import.md §4). */
  botUserAgent: string
}

/** Environment variables read (all optional). */
export const siteEnvKeys = [
  'VITE_SITE_URL',
  'VITE_APP_URL',
  'VITE_BRAND_NAME',
  'VITE_BRAND_HUE',
  'VITE_SUPPORT_EMAIL',
  'VITE_IMPRINT_URL',
  'VITE_PRIVACY_URL',
  'VITE_TERMS_URL',
  'VITE_DPA_URL',
  'VITE_BOT_USER_AGENT',
] as const

export type SiteEnv = Partial<Record<(typeof siteEnvKeys)[number], string>>

export const defaultSiteUrl = 'https://www.example.com'
export const defaultAppUrl = 'https://app.example.com'

const value = (raw: string | undefined) => {
  const trimmed = raw?.trim() ?? ''
  return trimmed === '' ? null : trimmed
}

/** An http(s) origin (no path, query or fragment), without trailing slash. */
function origin(name: string, raw: string | undefined, fallback: string): string {
  const text = value(raw) ?? fallback
  let url: URL
  try {
    url = new URL(text)
  } catch {
    throw new Error(`${name} must be an absolute http(s) URL, got "${text}"`)
  }
  if (!/^https?:$/.test(url.protocol) || url.username !== '' || url.password !== '') {
    throw new Error(`${name} must be an http(s) URL without credentials, got "${text}"`)
  }
  if ((url.pathname !== '/' && url.pathname !== '') || url.search !== '' || url.hash !== '') {
    throw new Error(`${name} must be an origin without path, query or fragment, got "${text}"`)
  }
  return url.origin
}

/** An absolute http(s) URL or nothing. */
function link(name: string, raw: string | undefined): string | null {
  const text = value(raw)
  if (text === null) return null
  let url: URL
  try {
    url = new URL(text)
  } catch {
    throw new Error(`${name} must be an absolute http(s) URL, got "${text}"`)
  }
  if (!/^https?:$/.test(url.protocol)) throw new Error(`${name} must be http(s), got "${text}"`)
  return url.href
}

export function resolveSiteConfig(
  env: SiteEnv,
  edition: { id: string; brand: { name: string; hue: number } },
): SiteConfig {
  const siteUrl = origin('VITE_SITE_URL', env.VITE_SITE_URL, defaultSiteUrl)
  const hueText = value(env.VITE_BRAND_HUE)
  const brandHue = hueText === null ? edition.brand.hue : Number(hueText)
  if (!Number.isFinite(brandHue) || brandHue < 0 || brandHue > 360) {
    throw new Error(`VITE_BRAND_HUE must be a number 0–360, got "${hueText}"`)
  }
  const supportEmail = value(env.VITE_SUPPORT_EMAIL)
  if (supportEmail !== null && !/^[^\s@<>"]+@[^\s@<>"]+\.[^\s@<>"]+$/.test(supportEmail)) {
    throw new Error(`VITE_SUPPORT_EMAIL must be an email address, got "${supportEmail}"`)
  }
  const botUserAgent = value(env.VITE_BOT_USER_AGENT) ?? `sCalenderPlusBot/1.0 (+${siteUrl}/bot)`
  if (!/^[A-Za-z][\w.-]*\/\S+/.test(botUserAgent)) {
    throw new Error(`VITE_BOT_USER_AGENT must start with "<Name>/<version>", got "${botUserAgent}"`)
  }
  return {
    edition: edition.id,
    siteUrl,
    appUrl: origin('VITE_APP_URL', env.VITE_APP_URL, defaultAppUrl),
    brandName: value(env.VITE_BRAND_NAME) ?? edition.brand.name,
    brandHue,
    supportEmail,
    imprintUrl: link('VITE_IMPRINT_URL', env.VITE_IMPRINT_URL),
    privacyUrl: link('VITE_PRIVACY_URL', env.VITE_PRIVACY_URL),
    termsUrl: link('VITE_TERMS_URL', env.VITE_TERMS_URL),
    dpaUrl: link('VITE_DPA_URL', env.VITE_DPA_URL),
    botUserAgent,
  }
}

/** The robots.txt token of the bot: its user agent's product name (`sCalenderPlusBot`). */
export const botToken = (userAgent: string) => userAgent.split('/')[0]!
