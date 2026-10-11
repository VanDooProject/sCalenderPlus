import { describe, expect, it } from 'vitest'
import caddyfile from '../../../../deploy/caddy/web.Caddyfile?raw'
import comboboxViewport from '../../node_modules/reka-ui/dist/Combobox/ComboboxViewport.js?raw'
import selectViewport from '../../node_modules/reka-ui/dist/Select/SelectViewport.js?raw'

/**
 * reka-ui's select and combobox viewports render a fixed `<style>` (hidden scrollbars). The strict
 * CSP (`deploy/caddy/web.Caddyfile`) allows exactly these by hash; this fails when an upgrade of
 * reka-ui changes the text, so the hashes are updated together with it.
 */
async function styleHash(source: string): Promise<string> {
  const match = /as: "style",[\s\S]*?createTextVNode\(("(?:[^"\\]|\\.)*")\)/.exec(source)
  if (!match) throw new Error('No inline style found')
  const text = JSON.parse(match[1]!) as string
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))
  return `'sha256-${btoa(String.fromCharCode(...new Uint8Array(digest)))}'`
}

describe('content security policy', () => {
  it.each([
    ['select', selectViewport],
    ['combobox', comboboxViewport],
  ])('allows the inline style of the %s viewport by hash', async (_name, source) => {
    expect(caddyfile).toContain(await styleHash(source))
  })
})
