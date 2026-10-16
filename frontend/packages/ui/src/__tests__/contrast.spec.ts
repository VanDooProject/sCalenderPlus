import { describe, expect, it } from 'vitest'
import tokens from '../tokens.css?raw'

/**
 * WCAG AA for the token pairs that carry text, for every brand hue (`--scal-brand-hue`, 0–360 in
 * steps of 5) in light and dark mode. The colors are read from tokens.css as written: custom
 * properties are substituted like the browser does, `calc`/`max`/`cos` evaluated, then OKLCH →
 * linear sRGB → relative luminance. Out-of-gamut colors are checked both clipped per channel and
 * gamut-mapped by reducing chroma (CSS Color 4), whichever gives the lower contrast.
 */

type Vars = Record<string, string>

/** The custom properties of the first block whose selector line matches `selector`. */
function block(selector: RegExp): Vars {
  const start = tokens.search(selector)
  if (start < 0) throw new Error(`No block ${selector}`)
  const open = tokens.indexOf('{', start)
  let depth = 0
  let end = open
  for (; end < tokens.length; end++) {
    if (tokens[end] === '{') depth++
    else if (tokens[end] === '}' && --depth === 0) break
  }
  const body = tokens.slice(open + 1, end).replace(/\/\*[\s\S]*?\*\//g, '')
  const vars: Vars = {}
  for (const match of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
    vars[match[1]!] = match[2]!.replace(/\s+/g, ' ').trim()
  }
  return vars
}

const theme = block(/@theme static\s*\{/)
const light = { ...theme, ...block(/^:root\s*\{/m) }
const dark = { ...light, ...block(/^:root\.dark\s*\{/m) }

function resolve(value: string, vars: Vars, depth = 0): string {
  if (depth > 10) throw new Error(`Cyclic custom property in ${value}`)
  const next = value.replace(/var\((--[\w-]+)\)/g, (_, name: string) => {
    const found = vars[name]
    if (found === undefined) throw new Error(`Undefined ${name}`)
    return `(${found})`
  })
  return next === value ? value : resolve(next, vars, depth + 1)
}

/** Evaluates a resolved CSS math expression (numbers, + - * /, calc, max, min, cos, deg). */
function evaluate(expression: string): number {
  const js = expression
    .replace(/(\d*\.?\d+)deg\b/g, '($1*Math.PI/180)')
    .replace(/\bcalc\(/g, '(')
    .replace(/\b(max|min|cos)\(/g, 'Math.$1(')
  if (!/^(?:[\d\s.+\-*/(),]|Math\.(?:max|min|cos|PI))*$/.test(js)) {
    throw new Error(`Unsupported expression: ${expression}`)
  }
  return Number(new Function(`return (${js})`)())
}

/** Top-level space-separated arguments of `oklch(...)` (alpha after `/` is dropped). */
function oklchArgs(value: string): [number, number, number] {
  let color = value.trim()
  // Substituted custom properties come wrapped in parentheses: `((oklch(…)))`.
  while (color.startsWith('(') && color.endsWith(')')) color = color.slice(1, -1).trim()
  if (!color.startsWith('oklch(') || !color.endsWith(')')) {
    throw new Error(`Not an oklch color: ${value}`)
  }
  const args: string[] = []
  let depth = 0
  let current = ''
  for (const char of color.slice('oklch('.length, -1)) {
    if (char === '(') depth++
    if (char === ')') depth--
    if (depth === 0 && (char === ' ' || char === '/')) {
      if (current) args.push(current)
      current = ''
      if (char === '/') break
      continue
    }
    current += char
  }
  if (current && args.length < 3) args.push(current)
  const [l, c, h] = args.map(evaluate)
  return [l!, c!, h!]
}

function oklchToLinearSrgb(l: number, c: number, h: number): [number, number, number] {
  const a = c * Math.cos((h * Math.PI) / 180)
  const b = c * Math.sin((h * Math.PI) / 180)
  const l_ = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m_ = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s_ = (l - 0.0894841775 * a - 1.291485548 * b) ** 3
  return [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ]
}

const inGamut = (rgb: number[]) => rgb.every((x) => x >= -1e-6 && x <= 1 + 1e-6)

function luminance(rgb: number[]): number {
  const [r, g, b] = rgb.map((x) => Math.min(1, Math.max(0, x)))
  return 0.2126 * r! + 0.7152 * g! + 0.0722 * b!
}

/** Luminance of an OKLCH color: clipped, and with chroma reduced into sRGB. */
function luminances([l, c, h]: [number, number, number]): number[] {
  const clipped = luminance(oklchToLinearSrgb(l, c, h))
  if (l >= 1 || l <= 0 || inGamut(oklchToLinearSrgb(l, c, h))) return [clipped]
  let low = 0
  let high = c
  for (let i = 0; i < 30; i++) {
    const mid = (low + high) / 2
    if (inGamut(oklchToLinearSrgb(l, mid, h))) low = mid
    else high = mid
  }
  return [clipped, luminance(oklchToLinearSrgb(l, low, h))]
}

function contrast(fg: number, bg: number): number {
  return (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05)
}

/** Text color → the backgrounds it is used on (semantic tokens, `--scal-*`). */
const textPairs: Record<string, string[]> = {
  '--scal-on-surface': [
    '--scal-surface',
    '--scal-surface-muted',
    '--scal-surface-raised',
    '--scal-surface-hover',
  ],
  // Secondary text also sits on hovered rows and buttons (`text-on-surface-muted hover:bg-surface-hover`).
  '--scal-on-surface-muted': [
    '--scal-surface',
    '--scal-surface-muted',
    '--scal-surface-raised',
    '--scal-surface-hover',
  ],
  // Links and the primary text color on page, sidebar, card and hovered rows.
  '--scal-primary': [
    '--scal-surface',
    '--scal-surface-muted',
    '--scal-surface-raised',
    '--scal-surface-hover',
  ],
  '--scal-on-primary': ['--scal-primary', '--scal-primary-hover'],
  '--scal-on-primary-soft': ['--scal-primary-soft'],
  '--scal-on-info-soft': ['--scal-info-soft'],
  '--scal-on-danger': ['--scal-danger', '--scal-danger-hover'],
  '--scal-on-danger-soft': ['--scal-danger-soft'],
  '--scal-on-success-soft': ['--scal-success-soft'],
  '--scal-on-warning-soft': ['--scal-warning-soft'],
}

const hues = Array.from({ length: 73 }, (_, i) => i * 5)

function failures(vars: Vars): string[] {
  const found: string[] = []
  for (const hue of hues) {
    const withHue = { ...vars, '--scal-brand-hue': String(hue) }
    const color = (name: string) => luminances(oklchArgs(resolve(`var(${name})`, withHue)))
    for (const [text, backgrounds] of Object.entries(textPairs)) {
      const fg = color(text)
      for (const background of backgrounds) {
        const bg = color(background)
        const ratio = Math.min(...fg.flatMap((f) => bg.map((b) => contrast(f, b))))
        if (ratio < 4.5) found.push(`hue ${hue}: ${text} on ${background} = ${ratio.toFixed(2)}`)
      }
    }
  }
  return found
}

describe('brand palette contrast', () => {
  it('reads the colors from tokens.css', () => {
    // The default indigo: white on primary as designed (about 6:1), the shade only where needed.
    const primary = oklchArgs(resolve('var(--scal-primary)', light))
    expect(primary[0]).toBeCloseTo(0.52, 5)
    expect(primary[2]).toBe(268)
    const green = oklchArgs(resolve('var(--scal-primary)', { ...light, '--scal-brand-hue': '180' }))
    expect(green[0]).toBeLessThan(0.46)
  })

  it('keeps WCAG AA for text on every hue in light mode', () => {
    expect(failures(light)).toEqual([])
  })

  it('keeps WCAG AA for text on every hue in dark mode', () => {
    expect(failures(dark)).toEqual([])
  })
})
