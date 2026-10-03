import { describe, expect, it, vi } from 'vitest'
import { defaultConfig, loadConfig, parseConfig } from '@/config'

describe('parseConfig', () => {
  it('keeps known string fields', () => {
    expect(parseConfig({ environment: 'staging', extra: 1 })).toEqual({ environment: 'staging' })
  })

  it.each([null, 'x', 42, {}, { environment: '' }, { environment: 7 }])(
    'falls back to defaults for %j',
    (raw) => {
      expect(parseConfig(raw)).toEqual(defaultConfig)
    },
  )
})

describe('loadConfig', () => {
  it('reads /config.json without caching', async () => {
    const fetchFn = vi
      .fn<typeof fetch>()
      .mockResolvedValue(new Response(JSON.stringify({ environment: 'production' })))

    await expect(loadConfig(fetchFn)).resolves.toEqual({ environment: 'production' })
    expect(fetchFn).toHaveBeenCalledWith(
      '/config.json',
      expect.objectContaining({ cache: 'no-store' }),
    )
  })

  it('uses defaults when the file is missing', async () => {
    const fetchFn = vi.fn<typeof fetch>().mockResolvedValue(new Response('', { status: 404 }))

    await expect(loadConfig(fetchFn)).resolves.toEqual(defaultConfig)
  })

  it('uses defaults when the request fails or the body is not JSON', async () => {
    await expect(
      loadConfig(vi.fn<typeof fetch>().mockRejectedValue(new TypeError())),
    ).resolves.toEqual(defaultConfig)
    await expect(
      loadConfig(vi.fn<typeof fetch>().mockResolvedValue(new Response('<html>'))),
    ).resolves.toEqual(defaultConfig)
  })
})
