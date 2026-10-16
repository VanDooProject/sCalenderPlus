// Types of check-edition-leak.mjs for its unit test (src/__tests__/editionLeak.spec.ts).
export interface SourceFile {
  path: string
  content: Uint8Array
}

export interface Markers {
  strings: string[]
  /** Content hash -> path of the non-text files of the edition. */
  hashes: Map<string, string>
}

export const MIN_MARKER_LENGTH: number
export function extractStrings(path: string, content: string): string[]
export function segments(text: string): string[]
export function collectMarkers(files: SourceFile[], shared: string[]): Markers
export function findLeaks(
  dist: SourceFile[],
  markers: Markers,
  forbiddenPaths?: string[],
): { file: string; marker: string }[]
export function checkBuild(options: { appDir: string; distDir: string; edition: string }): string[]
