import { inject, type InjectionKey } from 'vue'

/**
 * Runtime configuration, read from `/config.json` at startup so one image serves staging and
 * production. In containers the `web` service (Caddy) renders it from `PUBLIC_*` environment
 * variables; when it is absent (e.g. `vite dev`) the defaults apply.
 */
export interface AppConfig {
  /** Deployment environment name, e.g. `production`, `staging`, `development`. */
  environment: string
}

export const defaultConfig: Readonly<AppConfig> = Object.freeze({
  environment: import.meta.env.DEV ? 'development' : 'production',
})

export const configKey: InjectionKey<AppConfig> = Symbol('AppConfig')

/** Keeps only known, well-typed fields; unknown or malformed values fall back to defaults. */
export function parseConfig(raw: unknown): AppConfig {
  const source = typeof raw === 'object' && raw !== null ? (raw as Record<string, unknown>) : {}
  const environment = source.environment
  return {
    environment:
      typeof environment === 'string' && environment.trim() !== ''
        ? environment
        : defaultConfig.environment,
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

export function useConfig(): AppConfig {
  const config = inject(configKey)
  if (!config) {
    throw new Error('AppConfig not provided; call app.provide(configKey, config) before mount.')
  }
  return config
}
