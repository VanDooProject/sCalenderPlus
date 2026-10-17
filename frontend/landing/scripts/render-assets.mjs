#!/usr/bin/env node
// Renders the committed binary assets of the landing with headless Chromium (Playwright from e2e/):
//
//   - Open Graph images (1200×630) per edition and locale: src/assets/og/<locale>.png (standard)
//     and src/editions/<edition>/og/<locale>.png, from the edition's hero texts and hue. They carry
//     no product name, so a white-label build (VITE_BRAND_NAME) can keep them.
//   - The placeholder data processing agreement: public/downloads/dpa-template.pdf.
//
//   pnpm -C e2e install && pnpm -C frontend --filter landing render:assets
//
// PLAYWRIGHT_CHROMIUM_EXECUTABLE points to an installed Chromium if Playwright's own is missing.
// Run it after changing the hero texts or hues and commit the results; the build only copies them.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const e2eRequire = createRequire(resolve(root, '../../e2e/package.json'))
const { chromium } = e2eRequire('@playwright/test')
const font = pathToFileURL(
  createRequire(join(root, 'package.json')).resolve(
    '@fontsource-variable/inter/files/inter-latin-wght-normal.woff2',
  ),
).href

const readJson = (path) => JSON.parse(readFileSync(join(root, path), 'utf8'))
const locales = ['en', 'de']
const editions = {
  standard: { out: 'src/assets/og', overlay: null },
  poly: { out: 'src/editions/poly/og', overlay: 'src/editions/poly/locales' },
}

const escapeHtml = (text) => text.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`)

function ogHtml({ hue, eyebrow, title }) {
  const rows = [
    ['72%', 'oklch(0.93 0.045 HUE)'],
    ['54%', 'oklch(0.97 0.05 85)'],
    ['64%', 'oklch(0.96 0.035 155)'],
    ['46%', 'oklch(0.93 0.045 HUE)'],
  ]
    .map(
      ([width, color]) =>
        `<div class="row"><span class="dot"></span><span class="bar" style="width:${width}"></span><span class="chip" style="background:${color.replace('HUE', hue)}"></span></div>`,
    )
    .join('')
  return `<!doctype html><html><head><meta charset="utf-8"><style>
    @font-face { font-family: Inter; src: url(${font}) format('woff2'); font-weight: 100 900; }
    * { box-sizing: border-box; margin: 0; }
    body { width: 1200px; height: 630px; font-family: Inter, sans-serif; display: flex; align-items: center;
      background: linear-gradient(135deg, oklch(0.985 0.01 ${hue}) 0%, oklch(0.93 0.05 ${hue}) 100%); color: oklch(0.22 0.02 ${hue}); }
    .text { width: 620px; padding: 0 0 0 80px; display: flex; flex-direction: column; gap: 28px; }
    .eyebrow { font-size: 26px; font-weight: 650; letter-spacing: 0.04em; text-transform: uppercase; color: oklch(0.52 0.21 ${hue}); }
    h1 { font-size: 58px; line-height: 1.12; font-weight: 750; letter-spacing: -0.02em; }
    .card { margin-left: 60px; width: 400px; padding: 32px; border-radius: 28px; background: white;
      box-shadow: 0 20px 60px oklch(0.3 0.1 ${hue} / 0.18); display: flex; flex-direction: column; gap: 18px; }
    .head { height: 22px; width: 55%; border-radius: 11px; background: oklch(0.52 0.21 ${hue}); margin-bottom: 8px; }
    .row { display: flex; align-items: center; gap: 14px; padding: 14px; border-radius: 14px; border: 2px solid oklch(0.9 0.01 ${hue}); }
    .dot { width: 14px; height: 14px; border-radius: 7px; background: oklch(0.6 0.2 ${hue}); flex: none; }
    .bar { height: 14px; border-radius: 7px; background: oklch(0.87 0.02 ${hue}); }
    .chip { margin-left: auto; width: 64px; height: 24px; border-radius: 12px; flex: none; }
  </style></head><body>
    <div class="text"><p class="eyebrow">${escapeHtml(eyebrow)}</p><h1>${escapeHtml(title)}</h1></div>
    <div class="card"><div class="head"></div>${rows}</div>
  </body></html>`
}

const dpaHtml = `<!doctype html><html><head><meta charset="utf-8"><style>
  @font-face { font-family: Inter; src: url(${font}) format('woff2'); font-weight: 100 900; }
  body { font-family: Inter, sans-serif; font-size: 11pt; line-height: 1.5; color: #222; }
  h1 { font-size: 18pt; } h2 { font-size: 13pt; margin-top: 1.5em; }
  .note { border: 2px solid #c80; padding: 10px 14px; background: #fff7e0; }
</style></head><body>
  <h1>Data processing agreement (GDPR Art. 28) / Auftragsverarbeitungsvertrag (Art. 28 DSGVO)</h1>
  <p class="note"><strong>Placeholder / Platzhalter.</strong> This file is a placeholder shipped with the
  landing page. The operator replaces it with its own agreement (build variable VITE_DPA_URL).
  It contains no agreement and no company data. / Diese Datei ist ein Platzhalter der Landingpage. Der
  Betreiber ersetzt ihn durch seinen eigenen Vertrag (Build-Variable VITE_DPA_URL). Sie enthält keinen
  Vertrag und keine Unternehmensdaten.</p>
  <h2>What the operator's agreement covers / Was der Vertrag des Betreibers regelt</h2>
  <ol>
    <li>Parties: [controller (customer)] and [processor (operator)] / Parteien: [Verantwortlicher (Kunde)] und [Auftragsverarbeiter (Betreiber)]</li>
    <li>Subject, duration, nature and purpose of the processing / Gegenstand, Dauer, Art und Zweck der Verarbeitung</li>
    <li>Types of personal data and categories of data subjects / Arten personenbezogener Daten und Kategorien betroffener Personen</li>
    <li>Technical and organisational measures (Art. 32) / Technische und organisatorische Maßnahmen (Art. 32)</li>
    <li>Sub-processors: [hosting], [email delivery], [payment provider], [language model provider, if the importer is enabled] / Unterauftragsverarbeiter</li>
    <li>Support with data subject requests, deletion and return of data / Unterstützung bei Betroffenenanfragen, Löschung und Rückgabe der Daten</li>
    <li>Audits, notification of data breaches, liability / Kontrollen, Meldung von Datenpannen, Haftung</li>
  </ol>
</body></html>`

const browser = await chromium.launch({
  executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined,
})
try {
  const page = await browser.newPage({ viewport: { width: 1200, height: 630 } })
  for (const [edition, { out, overlay }] of Object.entries(editions)) {
    const { hue } = readJson(`src/editions/${edition}/brand.json`)
    mkdirSync(join(root, out), { recursive: true })
    for (const locale of locales) {
      const base = readJson(`src/locales/${locale}.json`).hero
      const own = overlay ? (readJson(`${overlay}/${locale}.json`).hero ?? {}) : {}
      const hero = { ...base, ...own }
      await page.setContent(ogHtml({ hue, eyebrow: hero.eyebrow, title: hero.title }))
      await page.evaluate(() => document.fonts.ready)
      await page.screenshot({ path: join(root, out, `${locale}.png`) })
      console.log(`${out}/${locale}.png`)
    }
  }
  await page.setContent(dpaHtml)
  await page.evaluate(() => document.fonts.ready)
  mkdirSync(join(root, 'public/downloads'), { recursive: true })
  writeFileSync(
    join(root, 'public/downloads/dpa-template.pdf'),
    await page.pdf({
      format: 'A4',
      margin: { top: '20mm', bottom: '20mm', left: '20mm', right: '20mm' },
    }),
  )
  console.log('public/downloads/dpa-template.pdf')
} finally {
  await browser.close()
}
