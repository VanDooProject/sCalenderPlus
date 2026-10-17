/**
 * The installable app (PWA): the web app manifest the build writes for its edition
 * (vite.config.ts `editionShell`) and the theme colour of a brand hue, also applied at runtime
 * (`applyBrand` in config.ts) when /config.json sets another hue. Pure functions, shared by the
 * build and the app.
 */

/** The PNG icons of an edition (its own `icons/` folder, else `src/assets/icons/`). */
export const iconFiles = [
  'icon-192.png',
  'icon-512.png',
  'icon-maskable-512.png',
  'apple-touch-icon.png',
] as const

/** Linear sRGB of an OKLCH colour (CSS Color 4). */
function oklchToLinearSrgb(l: number, c: number, hue: number): [number, number, number] {
  const h = (hue * Math.PI) / 180
  const a = c * Math.cos(h)
  const b = c * Math.sin(h)
  const l_ = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m_ = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s_ = (l - 0.0894841775 * a - 1.291485548 * b) ** 3
  return [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ]
}

const inGamut = (rgb: readonly number[]) => rgb.every((v) => v >= -1e-4 && v <= 1 + 1e-4)

function encode(linear: number): string {
  const v = Math.min(1, Math.max(0, linear))
  const srgb = v <= 0.0031308 ? 12.92 * v : 1.055 * v ** (1 / 2.4) - 0.055
  return Math.round(srgb * 255)
    .toString(16)
    .padStart(2, '0')
}

/** `#rrggbb` of an OKLCH colour; out-of-gamut colours lose chroma until they fit (CSS Color 4). */
export function oklchToHex(l: number, c: number, hue: number): string {
  let rgb = oklchToLinearSrgb(l, c, hue)
  if (!inGamut(rgb)) {
    let low = 0
    let high = c
    for (let i = 0; i < 24; i++) {
      const mid = (low + high) / 2
      if (inGamut(oklchToLinearSrgb(l, mid, hue))) low = mid
      else high = mid
    }
    rgb = oklchToLinearSrgb(l, low, hue)
  }
  return `#${rgb.map(encode).join('')}`
}

/**
 * The brand's primary colour (`--color-brand-600` of packages/ui/src/tokens.css: the same lightness
 * shade for green to cyan hues) as hex, for the manifest's `theme_color` and `<meta name="theme-color">`.
 */
export function brandThemeColor(hue: number): string {
  const shade = Math.max(0, 0.08 * Math.cos(((hue - 180) * Math.PI) / 180) - 0.006)
  return oklchToHex(0.52 - shade, 0.21, hue)
}

/** Light surface behind the splash screen (`--scal-surface`). */
export function brandBackgroundColor(hue: number): string {
  return oklchToHex(0.985, 0.003, hue)
}

/**
 * Navigations the service worker leaves to the network instead of answering with the app shell:
 * every path the web server proxies to the api (`/ical/` feeds, `/api/`, …) and the files it
 * renders per request.
 */
export function networkOnlyNavigations(proxiedPaths: readonly string[]): RegExp[] {
  const escaped = proxiedPaths.map((path) => path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
  return [
    // Workbox matches the path and query string.
    new RegExp(`^(?:${escaped.join('|')})(?:[/?]|$)`),
    /^\/(?:config\.json|healthz|manifest\.webmanifest)(?:\?|$)/,
  ]
}

export interface WebManifest {
  id: string
  name: string
  short_name: string
  start_url: string
  scope: string
  display: 'standalone'
  background_color: string
  theme_color: string
  icons: { src: string; sizes: string; type: string; purpose?: string }[]
}

/**
 * The manifest of an edition's brand (name and hue from its `brand.json`). The web image replaces
 * `name` and `short_name` with `PUBLIC_BRAND_NAME` when set (deploy/caddy/manifest.webmanifest.tmpl);
 * keep both on their own line as `"key": "value"`. No description: what the app is for stays off the
 * home screen (docs/product/editions.md §6.1).
 */
export function webManifest(brand: { name: string; hue: number }): WebManifest {
  return {
    id: '/',
    name: brand.name,
    short_name: brand.name,
    start_url: '/',
    scope: '/',
    display: 'standalone',
    background_color: brandBackgroundColor(brand.hue),
    theme_color: brandThemeColor(brand.hue),
    icons: [
      { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
      { src: '/icon-512.png', sizes: '512x512', type: 'image/png' },
      {
        src: '/icon-maskable-512.png',
        sizes: '512x512',
        type: 'image/png',
        purpose: 'maskable',
      },
      { src: '/favicon.svg', sizes: 'any', type: 'image/svg+xml' },
    ],
  }
}
