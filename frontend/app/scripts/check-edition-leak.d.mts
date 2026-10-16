// Types of check-edition-leak.mjs for its unit test (src/__tests__/editionLeak.spec.ts).
export interface SourceFile {
  path: string
  content: Uint8Array
}

export interface Markers {
  strings: string[]
  /** Content hash -> path of the asset files of the edition. */
  hashes: Map<string, string>
  /** Encoded form of an asset (base64, squashed SVG markup) -> its path. */
  encoded?: Map<string, string>
  /** Files of a type the check can't read (an error in an edition folder). */
  unsupported?: string[]
}

export const MIN_MARKER_LENGTH: number
export const LEAK_WORDS_FILE: string
export function extractStrings(path: string, content: string): string[]
export function segments(text: string): string[]
export function collectMarkers(files: SourceFile[], shared: string[]): Required<Markers>
export function dataUriPayloads(text: string): Uint8Array[]
export function findLeaks(
  dist: SourceFile[],
  markers: Markers,
  forbiddenPaths?: string[],
): { file: string; marker: string }[]
export function leakWords(files: SourceFile[]): string[]
export function findWords(
  dist: SourceFile[],
  words: string[],
): { file: string; word: string; context: string }[]
export function checkBuild(options: { appDir: string; distDir: string; edition: string }): string[]
