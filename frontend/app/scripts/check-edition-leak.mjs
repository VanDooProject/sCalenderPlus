#!/usr/bin/env node
// Edition leak check (docs: frontend/app/src/editions/types.ts). Fails when a build contains strings
// or files of another edition, e.g. poly wording in the standard (white-label) build.
//
//   node scripts/check-edition-leak.mjs [dist] [--edition standard]
//
// Markers are derived from the edition folders themselves, so they cannot drift from the code:
// every string literal of their .ts/.js/.vue files, every message of their JSON, cut at
// placeholders (the i18n plugin compiles `Leave {group}?` into separate pieces) and kept when it is
// long enough and not also part of the base locales or of the built edition. Other files (images,
// fonts, …) are matched by content hash. The edition folder's path must not appear either.
//
// As a self-test, the built edition's own markers must all be present in its build: otherwise the
// way strings end up in the bundle has changed and the check could pass without seeing anything.
import { createHash } from 'node:crypto'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

/** Shorter pieces (`Leave`, `Hi`) are too generic to tell editions apart. */
export const MIN_MARKER_LENGTH = 6

const codeExtensions = new Set(['.ts', '.mts', '.js', '.mjs', '.vue'])

function extension(path) {
  const dot = path.lastIndexOf('.')
  return dot === -1 ? '' : path.slice(dot).toLowerCase()
}

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

function jsonStrings(value) {
  if (typeof value === 'string') return [value]
  if (typeof value === 'object' && value !== null) return Object.values(value).flatMap(jsonStrings)
  return []
}

/** Every string a source file contributes (code literals, JSON values, template text). */
export function extractStrings(path, content) {
  const ext = extension(path)
  if (ext === '.json') return jsonStrings(JSON.parse(content))
  if (ext === '.vue') return vueStrings(content, path)
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

/**
 * Markers of an edition: the pieces of its strings that occur nowhere in `shared` (base locale
 * messages and the strings of the edition that was built), plus the hashes of its other files.
 * `files`: `{ path, content: Uint8Array }` relative to the edition folder.
 */
export function collectMarkers(files, shared) {
  const sharedText = shared.join('\n')
  const strings = new Set()
  const hashes = new Map()
  for (const { path, content } of files) {
    const ext = extension(path)
    if (ext === '.json' || codeExtensions.has(ext)) {
      for (const text of extractStrings(path, decode(content))) {
        for (const piece of segments(text)) {
          if (!sharedText.includes(piece)) strings.add(piece)
        }
      }
    } else {
      hashes.set(sha256(content), path)
    }
  }
  return { strings: [...strings].sort(), hashes }
}

const decoder = new TextDecoder()
const decode = (content) => decoder.decode(content)

function sha256(content) {
  return createHash('sha256').update(content).digest('hex')
}

/**
 * Where markers occur in a build. `dist`: `{ path, content: Uint8Array }`. Returns one entry per hit:
 * `{ file, marker }` (a string, an edition path or `file:<path>` for an identical file).
 */
export function findLeaks(dist, markers, forbiddenPaths = []) {
  const leaks = []
  for (const { path, content } of dist) {
    const text = decode(content)
    for (const marker of [...markers.strings, ...forbiddenPaths]) {
      if (text.includes(marker)) leaks.push({ file: path, marker })
    }
    const source = markers.hashes.get(sha256(content))
    if (source !== undefined) leaks.push({ file: path, marker: `file:${source}` })
  }
  return leaks
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
  return files.flatMap(({ path, content }) => extractStrings(path, decode(content)))
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
    if (markers.strings.length === 0 && markers.hashes.size === 0) {
      problems.push(`No markers found for the ${other} edition: is src/editions/${other} empty?`)
    }
    for (const { file, marker } of findLeaks(dist, markers, [
      `editions/${other}/`,
      `editions\\${other}\\`,
    ])) {
      problems.push(`${file}: contains ${JSON.stringify(marker)} of the ${other} edition`)
    }
  }

  // Self-test: the built edition's own string markers must be found in its build.
  const own = collectMarkers(sources[edition], [
    ...base,
    ...editions.filter((name) => name !== edition).flatMap((name) => sourceStrings(sources[name])),
  ])
  const ownLeaks = new Set(findLeaks(dist, { ...own, hashes: new Map() }).map((l) => l.marker))
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
