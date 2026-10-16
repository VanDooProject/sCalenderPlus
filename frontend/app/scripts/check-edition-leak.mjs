#!/usr/bin/env node
// Edition leak check (docs: docs/product/editions.md §2.2, frontend/app/src/editions/types.ts). Fails
// when a build contains strings or files of another edition, e.g. poly wording in the standard
// (white-label) build.
//
//   node scripts/check-edition-leak.mjs [dist] [--edition standard]
//
// Markers are derived from the edition folders themselves, so they cannot drift from the code:
// every string literal of their .ts/.js/.vue and .css files, every message of their JSON and the
// text of their .svg files, cut at placeholders (the i18n plugin compiles `Leave {group}?` into
// separate pieces) and kept when it is long enough and not also part of the base locales or of the
// built edition. Asset files (.svg, .css, images, fonts) are also matched as a whole: by content
// hash, as base64 and inside `data:` URIs (Vite inlines small assets: base64, SVG URL-encoded).
// Other file types in an edition folder are rejected. The edition folder's path must not appear.
//
// On top, an edition may list words that must never appear in another edition's build
// (`leak-words.json` in its own folder, so the list itself never ships), matched case-insensitively
// at the start of a word: `polyamor` finds "Polyamorous", and `poly` would not find "polyfill".
//
// As a self-test, the built edition's own markers must all be present in its build (and one of its
// own leak words, if it has a list): otherwise the way strings end up in the bundle has changed and
// the check could pass without seeing anything.
import { createHash } from 'node:crypto'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

/** Shorter pieces (`Leave`, `Hi`) are too generic to tell editions apart. */
export const MIN_MARKER_LENGTH = 6
/** Shortest encoded asset (base64, squashed SVG) searched for as text. */
const MIN_ENCODED_LENGTH = 24

/** The leak-word list of an edition folder: configures this check, never bundled. */
export const LEAK_WORDS_FILE = 'leak-words.json'

const codeExtensions = new Set(['.ts', '.mts', '.js', '.mjs', '.vue'])
/** Text assets: their strings are markers and the file is matched as a whole. */
const textAssetExtensions = new Set(['.css', '.svg'])
/** Binary assets: matched as a whole (hash, base64, data: URI). */
const binaryAssetExtensions = new Set([
  '.png',
  '.jpg',
  '.jpeg',
  '.gif',
  '.webp',
  '.avif',
  '.ico',
  '.woff',
  '.woff2',
])
/** Build output searched for leak words (binary files could contain them by chance). */
const distTextExtensions = new Set([
  '.html',
  '.js',
  '.mjs',
  '.css',
  '.json',
  '.svg',
  '.txt',
  '.webmanifest',
  '.xml',
  '.map',
])

function extension(path) {
  const dot = path.lastIndexOf('.')
  return dot === -1 ? '' : path.slice(dot).toLowerCase()
}

const isLeakWordsFile = (path) => path === LEAK_WORDS_FILE

/** String literals of TypeScript/JavaScript source, without import/export specifiers. */
function codeStrings(source, fileName) {
  const file = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true)
  const strings = []
  const visit = (node) => {
    if (
      (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) &&
      node.moduleSpecifier !== undefined
    ) {
      return
    }
    if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) return
    if (
      ts.isStringLiteral(node) ||
      ts.isNoSubstitutionTemplateLiteral(node) ||
      ts.isTemplateHead(node) ||
      ts.isTemplateMiddle(node) ||
      ts.isTemplateTail(node)
    ) {
      strings.push(node.text)
    }
    ts.forEachChild(node, visit)
  }
  visit(file)
  return strings
}

/** `<script>` blocks as code; the template's text and its user-visible static attributes. */
function vueStrings(source, fileName) {
  const strings = []
  const withoutScripts = source.replace(/<script\b[^>]*>([\s\S]*?)<\/script>/g, (_, script) => {
    strings.push(...codeStrings(script, `${fileName}.ts`))
    return ''
  })
  const template = withoutScripts.replace(/<style\b[^>]*>[\s\S]*?<\/style>/g, '')
  for (const match of template.matchAll(/>([^<]+)</g)) strings.push(match[1])
  for (const match of template.matchAll(
    /\s(?:alt|title|label|placeholder|aria-label)="([^"]*)"/g,
  )) {
    strings.push(match[1])
  }
  return strings
}

/** Quoted strings of a stylesheet (`content: "…"`, custom properties), without comments. */
function cssStrings(source) {
  const css = source.replace(/\/\*[\s\S]*?\*\//g, '')
  const strings = []
  for (const match of css.matchAll(/"((?:[^"\\\n]|\\.)*)"|'((?:[^'\\\n]|\\.)*)'/g)) {
    strings.push((match[1] ?? match[2]).replace(/\\(.)/g, '$1'))
  }
  return strings
}

/** Text and labelling attributes of an SVG (not path data: shapes are often shared). */
function svgStrings(source) {
  const svg = source.replace(/<!--[\s\S]*?-->/g, '')
  const strings = []
  for (const match of svg.matchAll(/>([^<]+)</g)) strings.push(match[1])
  for (const match of svg.matchAll(/\s(?:aria-label|title|alt)="([^"]*)"/g)) strings.push(match[1])
  return strings.map((s) => s.trim()).filter((s) => s !== '')
}

function jsonStrings(value) {
  if (typeof value === 'string') return [value]
  if (typeof value === 'object' && value !== null) return Object.values(value).flatMap(jsonStrings)
  return []
}

/** Every string a source file contributes (code literals, JSON values, template/CSS/SVG text). */
export function extractStrings(path, content) {
  const ext = extension(path)
  if (ext === '.json') return jsonStrings(JSON.parse(content))
  if (ext === '.vue') return vueStrings(content, path)
  if (ext === '.css') return cssStrings(content)
  if (ext === '.svg') return svgStrings(content)
  if (codeExtensions.has(ext)) return codeStrings(content, path)
  return []
}

/** The pieces of a string as they appear in the bundle: split at `{placeholders}` and plural `|`. */
export function segments(text) {
  return text
    .split(/\{[^{}]*\}|\$\{[^{}]*\}|\|/)
    .map((piece) => piece.trim())
    .filter((piece) => piece.length >= MIN_MARKER_LENGTH && /\p{L}/u.test(piece))
}

const decoder = new TextDecoder()
const decode = (content) => decoder.decode(content)

function sha256(content) {
  return createHash('sha256').update(content).digest('hex')
}

/**
 * Markup without whitespace (also escaped, as in a JS string) and with one quote style (Vite's SVG
 * data URIs use `'`).
 */
function squash(text) {
  return text.replace(/\\?["']/g, "'").replace(/\\[nrt]|\s+/g, '')
}

/**
 * Markers of an edition: the pieces of its strings that occur nowhere in `shared` (base locale
 * messages and the strings of the edition that was built), plus its asset files as a whole (content
 * hash, base64 and, for SVG, the squashed markup). `files`: `{ path, content: Uint8Array }` relative
 * to the edition folder. Files of a type the check can't see into are listed in `unsupported`.
 */
export function collectMarkers(files, shared) {
  const sharedText = shared.join('\n')
  const strings = new Set()
  const hashes = new Map()
  const encoded = new Map()
  const unsupported = []
  for (const { path, content } of files) {
    if (isLeakWordsFile(path)) continue
    const ext = extension(path)
    const asset = textAssetExtensions.has(ext) || binaryAssetExtensions.has(ext)
    const textual = ext === '.json' || codeExtensions.has(ext) || textAssetExtensions.has(ext)
    if (!asset && !textual) {
      unsupported.push(path)
      continue
    }
    if (textual) {
      for (const text of extractStrings(path, decode(content))) {
        for (const piece of segments(text)) {
          if (!sharedText.includes(piece)) strings.add(piece)
        }
      }
    }
    if (asset) {
      hashes.set(sha256(content), path)
      const forms = [Buffer.from(content).toString('base64')]
      if (ext === '.svg') forms.push(squash(decode(content)))
      // A tiny file's encoding could occur by chance; its hash still counts.
      for (const form of forms) if (form.length >= MIN_ENCODED_LENGTH) encoded.set(form, path)
    }
  }
  return { strings: [...strings].sort(), hashes, encoded, unsupported }
}

/** The payloads of the `data:` URIs in a text, decoded (base64 or percent-encoded). */
export function dataUriPayloads(text) {
  const payloads = []
  for (const match of text.matchAll(
    /data:[\w.+/-]*((?:;[\w.+-]+=[\w.+-]+)*)(;base64)?,([^"`\s)]*)/gi,
  )) {
    const [, , base64, data] = match
    if (base64) {
      payloads.push(new Uint8Array(Buffer.from(data.replace(/[^A-Za-z0-9+/=]/g, ''), 'base64')))
    } else {
      const latin1 = data.replace(/%([0-9a-f]{2})/gi, (_, hex) =>
        String.fromCharCode(parseInt(hex, 16)),
      )
      payloads.push(new Uint8Array(Buffer.from(latin1, 'latin1')))
    }
  }
  return payloads
}

/**
 * Where markers occur in a build. `dist`: `{ path, content: Uint8Array }`. Returns one entry per hit:
 * `{ file, marker }` (a string, an edition path, or `file:<path>` for an asset that was copied,
 * inlined as a data: URI or embedded as base64). Text inside data: URIs is searched too.
 */
export function findLeaks(dist, markers, forbiddenPaths = []) {
  const leaks = []
  const encoded = [...(markers.encoded ?? new Map())]
  for (const { path, content } of dist) {
    const payloads = dataUriPayloads(decode(content))
    const texts = [decode(content), ...payloads.map(decode)]
    for (const marker of [...markers.strings, ...forbiddenPaths]) {
      if (texts.some((text) => text.includes(marker))) leaks.push({ file: path, marker })
    }
    const assets = new Set()
    for (const bytes of [content, ...payloads]) {
      const source = markers.hashes.get(sha256(bytes))
      if (source !== undefined) assets.add(source)
    }
    if (encoded.length > 0) {
      const squashed = texts.map(squash)
      for (const [form, source] of encoded) {
        if ([...texts, ...squashed].some((text) => text.includes(form))) assets.add(source)
      }
    }
    for (const source of assets) leaks.push({ file: path, marker: `file:${source}` })
  }
  return leaks
}

/** An edition's leak words (`leak-words.json`: `{ "words": [...] }`); empty without the file. */
export function leakWords(files) {
  const file = files.find(({ path }) => isLeakWordsFile(path))
  if (file === undefined) return []
  const words = JSON.parse(decode(file.content)).words
  if (!Array.isArray(words) || words.some((w) => typeof w !== 'string' || w.trim() === '')) {
    throw new Error(`${LEAK_WORDS_FILE}: "words" must be a list of non-empty strings`)
  }
  return words.map((w) => w.trim())
}

/**
 * Leak words in the text files of a build (and their data: URIs), case-insensitively at the start of
 * a word: `polyamor` finds "Polyamorous", `kink` not "Skinks". One `{ file, word, context }` per word
 * and file.
 */
export function findWords(dist, words) {
  if (words.length === 0) return []
  const escaped = words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
  const pattern = new RegExp(`(?<![\\p{L}\\p{N}_])(?:${escaped.join('|')})`, 'giu')
  const hits = []
  for (const { path, content } of dist) {
    if (!distTextExtensions.has(extension(path))) continue
    const text = decode(content)
    const seen = new Set()
    for (const part of [text, ...dataUriPayloads(text).map(decode)]) {
      for (const match of part.matchAll(pattern)) {
        const word = words.find((w) => w.toLowerCase() === match[0].toLowerCase())
        if (word === undefined || seen.has(word)) continue
        seen.add(word)
        const context = part.slice(
          Math.max(0, match.index - 20),
          match.index + match[0].length + 20,
        )
        hits.push({ file: path, word, context })
      }
    }
  }
  return hits
}

function readTree(root) {
  const files = []
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name)
      if (entry.isDirectory()) walk(path)
      else if (entry.isFile()) {
        files.push({ path: relative(root, path).split(sep).join('/'), content: readFileSync(path) })
      }
    }
  }
  walk(root)
  return files
}

function sourceStrings(files) {
  return files
    .filter(({ path }) => !isLeakWordsFile(path))
    .flatMap(({ path, content }) => extractStrings(path, decode(content)))
}

/** Runs the check; returns the problems found (empty: the build is clean). */
export function checkBuild({ appDir, distDir, edition }) {
  const editionsDir = join(appDir, 'src', 'editions')
  const editions = readdirSync(editionsDir).filter((name) =>
    statSync(join(editionsDir, name)).isDirectory(),
  )
  if (!editions.includes(edition)) {
    return [`Unknown edition "${edition}" (expected one of ${editions.join(', ')})`]
  }
  const base = readTree(join(appDir, 'src', 'locales')).flatMap(({ path, content }) =>
    extractStrings(path, decode(content)),
  )
  const sources = Object.fromEntries(
    editions.map((name) => [name, readTree(join(editionsDir, name))]),
  )
  const dist = readTree(distDir)
  const problems = []

  for (const other of editions.filter((name) => name !== edition)) {
    const markers = collectMarkers(sources[other], [...base, ...sourceStrings(sources[edition])])
    for (const path of markers.unsupported) {
      problems.push(
        `src/editions/${other}/${path}: file type not supported in an edition folder (the leak ` +
          'check reads code, JSON, CSS, SVG, images and fonts; extend check-edition-leak.mjs)',
      )
    }
    if (markers.strings.length === 0 && markers.hashes.size === 0) {
      problems.push(`No markers found for the ${other} edition: is src/editions/${other} empty?`)
    }
    for (const { file, marker } of findLeaks(dist, markers, [
      `editions/${other}/`,
      `editions\\${other}\\`,
    ])) {
      problems.push(`${file}: contains ${JSON.stringify(marker)} of the ${other} edition`)
    }
    for (const { file, word, context } of findWords(dist, leakWords(sources[other]))) {
      problems.push(
        `${file}: contains ${JSON.stringify(word)} (…${context}…) from the ${other} edition's ` +
          LEAK_WORDS_FILE,
      )
    }
  }

  // Self-test: the built edition's own string markers (and leak words) must be found in its build.
  const own = collectMarkers(sources[edition], [
    ...base,
    ...editions.filter((name) => name !== edition).flatMap((name) => sourceStrings(sources[name])),
  ])
  const ownLeaks = new Set(
    findLeaks(dist, { ...own, hashes: new Map(), encoded: new Map() }).map((l) => l.marker),
  )
  const missing = own.strings.filter((marker) => !ownLeaks.has(marker))
  if (own.strings.length === 0) {
    problems.push(`No markers found for the built ${edition} edition: self-test impossible`)
  }
  for (const marker of missing) {
    problems.push(
      `Self-test: ${JSON.stringify(marker)} of the built ${edition} edition is not in the build ` +
        '(was the right edition built, or did the bundle format change?)',
    )
  }
  const ownWords = leakWords(sources[edition])
  if (ownWords.length > 0 && findWords(dist, ownWords).length === 0) {
    problems.push(
      `Self-test: none of the ${edition} edition's own leak words is in its build ` +
        `(src/editions/${edition}/${LEAK_WORDS_FILE})`,
    )
  }
  return problems
}

function main(argv) {
  const args = [...argv]
  let edition = 'standard'
  const editionFlag = args.indexOf('--edition')
  if (editionFlag !== -1) {
    edition = args[editionFlag + 1] ?? ''
    args.splice(editionFlag, 2)
  }
  const appDir = resolve(fileURLToPath(new URL('..', import.meta.url)))
  const distDir = resolve(args[0] ?? join(appDir, 'dist'))
  const problems = checkBuild({ appDir, distDir, edition })
  if (problems.length > 0) {
    for (const problem of problems) console.error(`::error::edition leak check: ${problem}`)
    console.error(`Edition leak check failed for ${distDir} (${edition}).`)
    return 1
  }
  console.log(`Edition leak check passed: ${distDir} (${edition}) has no other edition's strings.`)
  return 0
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
