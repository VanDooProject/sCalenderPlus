import { inject, type InjectionKey } from 'vue'
import { edition } from '@edition'
import { brandThemeColor } from '@/lib/webManifest'

/**
 * Runtime branding (white-label per deployment, no rebuild). Unset or invalid values fall back to
 * the defaults of the build's edition.
 */
export interface BrandConfig {
  /** Product name: brand mark, document title and messages that name the app. */
  name: string
  /** Logo shown instead of the built-in mark: an https URL or a same-origin path. */
  logoUrl: string | null
  /** OKLCH hue (0–360) of the brand palette (`--scal-brand-hue`). */
  hue: number
  supportEmail: string | null
  imprintUrl: string | null
  privacyUrl: string | null
  termsUrl: string | null
}

/**
 * Runtime configuration, read from `/config.json` at startup so one image serves staging and
 * production. In containers the `web` service (Caddy) renders it from `PUBLIC_*` environment
 * variables; when it is absent (e.g. `vite dev`) the defaults apply.
 */
export interface AppConfig {
  /** Deployment environment name, e.g. `production`, `staging`, `development`. */
  environment: string
  brand: BrandConfig
}

export const defaultBrand: Readonly<BrandConfig> = Object.freeze({
  name: edition.brand.name,
  logoUrl: null,
  hue: edition.brand.hue,
  supportEmail: null,
  imprintUrl: null,
  privacyUrl: null,
  termsUrl: null,
})

export const defaultConfig: Readonly<AppConfig> = Object.freeze({
  environment: import.meta.env.DEV ? 'development' : 'production',
  brand: defaultBrand,
})

export const configKey: InjectionKey<AppConfig> = Symbol('AppConfig')

const maxNameLength = 60
const maxUrlLength = 2048
const maxEmailLength = 254

function record(raw: unknown): Record<string, unknown> {
  return typeof raw === 'object' && raw !== null && !Array.isArray(raw)
    ? (raw as Record<string, unknown>)
    : {}
}

function text(raw: unknown, maxLength: number): string | null {
  if (typeof raw !== 'string') return null
  const value = raw.trim()
  // No control characters (they never belong in a name, URL or address).
  return value !== '' && value.length <= maxLength && !/[\u0000-\u001f\u007f]/.test(value)
    ? value
    : null
}

/** An absolute https URL, or a same-origin absolute path (`/brand/logo.svg`, not `//host`). */
export function parseBrandUrl(raw: unknown): string | null {
  const value = text(raw, maxUrlLength)
  if (value === null || /\s|\\/.test(value)) return null
  if (value.startsWith('/')) {
    return value.startsWith('//') ? null : value
  }
  try {
    const url = new URL(value)
    return url.protocol === 'https:' && url.hostname !== '' && !url.username && !url.password
      ? value
      : null
  } catch {
    return null
  }
}

/**
 * The https logo URLs whose origin the web server adds to the CSP's img-src: the
 * `brand_logo_origin` map in deploy/caddy/web.Caddyfile, same rule (config.spec.ts checks both
 * against the same cases). An ASCII host name and an optional port only, so no IDN (`bücher.de`),
 * IPv6 literal or underscore: a logo the CSP would block falls back to the built-in mark instead
 * of showing as a broken image.
 */
export const cspLogoUrlPattern = /^https:\/\/[A-Za-z0-9.-]+(?::[0-9]+)?(?:[/?#]|$)/i

/** `parseBrandUrl`, limited to what the CSP allows for images (a same-origin path or see above). */
export function parseBrandLogoUrl(raw: unknown): string | null {
  const value = parseBrandUrl(raw)
  return value === null || value.startsWith('/') || cspLogoUrlPattern.test(value) ? value : null
}

export function parseBrandEmail(raw: unknown): string | null {
  const value = text(raw, maxEmailLength)
  return value !== null && /^[^\s@<>()",;:]+@[^\s@<>()",;:]+\.[^\s@<>()",;:]+$/.test(value)
    ? value
    : null
}

export function parseBrandHue(raw: unknown): number | null {
  return typeof raw === 'number' && Number.isFinite(raw) && raw >= 0 && raw <= 360 ? raw : null
}

/** Each field on its own: an invalid value falls back to the edition default, the rest is kept. */
export function parseBrand(raw: unknown): BrandConfig {
  const source = record(raw)
  return {
    name: text(source.name, maxNameLength) ?? defaultBrand.name,
    logoUrl: parseBrandLogoUrl(source.logoUrl),
    hue: parseBrandHue(source.hue) ?? defaultBrand.hue,
    supportEmail: parseBrandEmail(source.supportEmail),
    imprintUrl: parseBrandUrl(source.imprintUrl),
    privacyUrl: parseBrandUrl(source.privacyUrl),
    termsUrl: parseBrandUrl(source.termsUrl),
  }
}

/** Keeps only known, well-typed fields; unknown or malformed values fall back to defaults. */
export function parseConfig(raw: unknown): AppConfig {
  const source = record(raw)
  const environment = source.environment
  return {
    environment:
      typeof environment === 'string' && environment.trim() !== ''
        ? environment
        : defaultConfig.environment,
    brand: parseBrand(source.brand),
  }
}

export async function loadConfig(fetchFn: typeof fetch = fetch): Promise<AppConfig> {
  try {
    const response = await fetchFn('/config.json', {
      cache: 'no-store',
      headers: { Accept: 'application/json' },
    })
    if (!response.ok) {
      return { ...defaultConfig }
    }
    return parseConfig(await response.json())
  } catch {
    return { ...defaultConfig }
  }
}

/**
 * Applies the brand outside the Vue tree: the palette hue as a custom property on <html> (CSSOM,
 * allowed by the strict CSP, unlike a `style` attribute in markup), the browser's theme colour
 * (the build writes the edition's into index.html and the manifest) and the initial document title
 * (App.vue keeps it in step with the route).
 */
export function applyBrand(brand: BrandConfig, root: HTMLElement = document.documentElement) {
  root.style.setProperty('--scal-brand-hue', String(brand.hue))
  document
    .querySelector('meta[name="theme-color"]')
    ?.setAttribute('content', brandThemeColor(brand.hue))
  document.title = brand.name
}

export function useConfig(): AppConfig {
  const config = inject(configKey)
  if (!config) {
    throw new Error('AppConfig not provided; call app.provide(configKey, config) before mount.')
  }
  return config
}

export function useBrand(): BrandConfig {
  return useConfig().brand
}
