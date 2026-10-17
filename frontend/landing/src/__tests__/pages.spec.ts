import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createHead } from '@unhead/vue/client'
import App from '@/App.vue'
import { routes } from '@/router'
import { plans, site } from '@/site'

async function render(path: string) {
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(App, { global: { plugins: [router, createHead()] } })
  await flushPromises()
  return wrapper
}

describe('home page', () => {
  it('links the sign-up of the web app and shows the features', async () => {
    const page = await render('/')
    expect(page.get('h1').text()).toContain('One calendar for everyone')
    expect(page.get('[data-testid="hero-cta"]').attributes('href')).toBe(`${site.appUrl}/register`)
    expect(page.findAll('#features li')).toHaveLength(6)
  })

  it('switches the demo between viewers: busy entries lose their title, hidden ones vanish', async () => {
    const page = await render('/')
    const titles = () => page.findAll('[data-testid="demo-event"]').map((e) => e.text())
    expect(titles()).toHaveLength(4)
    expect(page.find('[data-testid="demo-hidden"]').exists()).toBe(false)

    const buttons = page.findAll('[data-testid="demo-viewers"] button')
    await buttons[1]!.trigger('click')
    expect(buttons[1]!.attributes('aria-pressed')).toBe('true')
    expect(titles().join(' ')).not.toContain('Board meeting')
    expect(titles().join(' ')).toContain('Busy')

    await buttons[2]!.trigger('click')
    expect(titles()).toHaveLength(2)
    expect(page.get('[data-testid="demo-hidden"]').text()).toBe('2 entries hidden')
  })

  it('builds the pricing table from the plan data', async () => {
    const page = await render('/')
    const pro = plans.plans.find((p) => p.id === 'pro')!
    expect(page.get('[data-testid="plan-pro"] [data-testid="plan-price"]').text()).toBe(
      `€${pro.price.monthly}`,
    )
    await page.get('[data-testid="period-yearly"]').trigger('click')
    expect(page.get('[data-testid="plan-pro"] [data-testid="plan-price"]').text()).toBe(
      `€${pro.price.yearly}`,
    )
    const free = plans.plans.find((p) => p.id === 'free')!
    const overrides = page.get('[data-testid="row-eventsWithOverrides"]').findAll('td')
    expect(overrides.map((td) => td.text())).toEqual([
      String(free.limits.eventsWithOverrides),
      'Unlimited',
      'Unlimited',
    ])
    expect(page.findAll('[data-testid^="row-"]')).toHaveLength(13)
  })

  it('renders German under /de with German links', async () => {
    const page = await render('/de')
    expect(page.get('h1').text()).toContain('Ein Kalender für alle')
    expect(page.get('[data-testid="plan-pro"] [data-testid="plan-price"]').text()).toBe('12 €')
    expect(page.get('[data-testid="footer-terms"]').attributes('href')).toBe('/de/legal/terms')
    expect(page.get('[data-testid="language-switch"]').attributes('href')).toBe('/')
  })
})

describe('footer', () => {
  it('links every legal page and the bot page', async () => {
    const page = await render('/bot')
    for (const [doc, href] of [
      ['imprint', '/legal/imprint'],
      ['privacy', '/legal/privacy'],
      ['terms', '/legal/terms'],
      ['dpa', '/legal/dpa'],
      ['bot', '/bot'],
    ]) {
      expect(page.get(`[data-testid="footer-${doc}"]`).attributes('href')).toBe(href)
    }
    expect(page.get('[data-testid="language-switch"]').attributes('href')).toBe('/de/bot')
  })
})

describe('legal pages', () => {
  it.each(['imprint', 'privacy', 'terms', 'dpa'])(
    '%s is a template marked for the operator',
    async (doc) => {
      const page = await render(`/legal/${doc}`)
      expect(page.find(`[data-testid="legal-${doc}"]`).exists()).toBe(true)
      expect(page.get('[data-testid="legal-template"]').text()).toContain(
        'Template for the operator',
      )
      expect(page.findAll(`[data-testid="legal-${doc}"] section`).length).toBeGreaterThanOrEqual(3)
      expect(page.text()).toContain('[')
    },
  )

  it('offers the placeholder DPA download', async () => {
    const page = await render('/de/legal/dpa')
    const link = page.get('[data-testid="dpa-download"]')
    expect(link.attributes('href')).toBe('/downloads/dpa-template.pdf')
    expect(link.attributes('download')).toBeDefined()
  })
})

describe('bot page', () => {
  it('documents the user agent, robots.txt rules and contact', async () => {
    const page = await render('/bot')
    expect(page.get('[data-testid="bot-user-agent"]').text()).toBe(site.botUserAgent)
    expect(page.get('[data-testid="bot-robots"]').text()).toBe(
      'User-agent: sCalenderPlusBot\nDisallow: /',
    )
    expect(page.text()).toContain('one request every 10 seconds per domain')
    expect(page.text()).toContain('robots.txt')
    expect(page.get('[data-testid="bot-contact"]').text()).toBe('[Contact address of the operator]')
  })
})

describe('not found', () => {
  it('renders the not-found page for unknown paths', async () => {
    const page = await render('/nope')
    expect(page.get('h1').text()).toBe('Page not found')
  })
})
