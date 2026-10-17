#!/usr/bin/env node
// Checks the prerendered landing after `vite-ssg build` (part of `pnpm build`):
//   - every page of src/lib/routes.ts exists as static HTML with its locale, title, description,
//     canonical URL, hreflang translations and Open Graph image;
//   - nothing inline that the CSP of deploy/caddy/landing.Caddyfile would block (no inline scripts,
//     no <style> elements or style attributes);
//   - robots.txt, sitemap.xml, favicon and the OG images are there.
//
//   node scripts/check-dist.mjs [dist]
import { existsSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(fileURLToPath(new URL('..', import.meta.url)))
const dist = resolve(process.argv[2] ?? join(root, 'dist'))

// The page list mirrors src/lib/routes.ts (flat files: /bot -> bot.html, /de -> de.html).
const locales = ['en', 'de']
const pages = ['', 'legal/terms', 'legal/privacy', 'legal/imprint', 'legal/dpa', 'bot']
const fileOf = (path) => (path === '/' ? 'index.html' : `${path.slice(1)}.html`)
const pathOf = (locale, page) => {
  const prefix = locale === 'en' ? '' : `/${locale}`
  if (page === '') return prefix === '' ? '/' : prefix
  return `${prefix}/${page}`
}

const problems = []
const attr = (html, pattern) => pattern.exec(html)?.[1] ?? null

function checkInline(file, html) {
  for (const match of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
    const [, attrs, body] = match
    if (!/\ssrc=/.test(attrs) || body.trim() !== '') {
      problems.push(`${file}: inline script (blocked by the CSP): ${match[0].slice(0, 80)}`)
    }
  }
  if (/<style\b/i.test(html)) problems.push(`${file}: <style> element (blocked by the CSP)`)
  const style = /<[^>]+\sstyle=["'][^"']*["']/i.exec(html)
  if (style)
    problems.push(`${file}: style attribute (blocked by the CSP): ${style[0].slice(0, 80)}`)
}

for (const locale of locales) {
  for (const page of pages) {
    const path = pathOf(locale, page)
    const file = fileOf(path)
    if (!existsSync(join(dist, file))) {
      problems.push(`${file}: missing (page ${path})`)
      continue
    }
    const html = readFileSync(join(dist, file), 'utf8')
    checkInline(file, html)
    if (attr(html, /<html[^>]*\slang="([^"]+)"/) !== locale)
      problems.push(`${file}: lang is not ${locale}`)
    if (!/<title>[^<]+<\/title>/.test(html)) problems.push(`${file}: no <title>`)
    if (!/<meta name="description" content="[^"]+"/.test(html))
      problems.push(`${file}: no description`)
    const canonical = attr(html, /<link rel="canonical" href="([^"]+)"/)
    if (canonical === null || !canonical.endsWith(path === '/' ? '/' : path)) {
      problems.push(`${file}: canonical ${canonical} does not end with ${path}`)
    }
    for (const hreflang of [...locales, 'x-default']) {
      if (!new RegExp(`<link rel="alternate" hreflang="${hreflang}" href="[^"]+"`).test(html)) {
        problems.push(`${file}: no hreflang ${hreflang}`)
      }
    }
    const image = attr(html, /<meta property="og:image" content="([^"]+)"/)
    if (image === null || !image.endsWith(`/og/${locale}.png`))
      problems.push(`${file}: og:image ${image}`)
    if (/data-server-rendered="true"><\/div>/.test(html))
      problems.push(`${file}: empty app container`)
  }
}

if (existsSync(join(dist, '404.html'))) {
  const html = readFileSync(join(dist, '404.html'), 'utf8')
  checkInline('404.html', html)
  if (!/<meta name="robots" content="noindex"/.test(html)) problems.push('404.html: not noindex')
} else {
  problems.push('404.html: missing')
}

for (const file of [
  'robots.txt',
  'sitemap.xml',
  'favicon.svg',
  'theme-init.js',
  'og/en.png',
  'og/de.png',
]) {
  if (!existsSync(join(dist, file))) problems.push(`${file}: missing`)
}

if (problems.length > 0) {
  for (const problem of problems) console.error(`::error::landing dist check: ${problem}`)
  process.exitCode = 1
} else {
  console.log(`Landing dist check passed: ${dist}`)
}
