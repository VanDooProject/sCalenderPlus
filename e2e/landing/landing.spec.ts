import { expect, test } from '@playwright/test'
import { expectAccessible } from '../support/a11y'

// The prerendered landing (frontend/landing), standard edition, served by `vite preview`.
const pages = [
  ['/', 'One calendar for everyone'],
  ['/de', 'Ein Kalender für alle'],
  ['/legal/terms', 'Terms of use'],
  ['/legal/privacy', 'Privacy policy'],
  ['/legal/imprint', 'Imprint'],
  ['/legal/dpa', 'Data processing agreement'],
  ['/bot', 'sCalenderPlusBot'],
  ['/de/legal/terms', 'Nutzungsbedingungen'],
  ['/de/legal/privacy', 'Datenschutzerklärung'],
  ['/de/legal/imprint', 'Impressum'],
  ['/de/legal/dpa', 'Auftragsverarbeitungsvertrag'],
  ['/de/bot', 'sCalenderPlusBot'],
] as const

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`accessibility (${scheme})`, () => {
    test.use({ colorScheme: scheme })
    for (const [path, heading] of pages) {
      test(`${path} has no serious axe violations`, async ({ page }) => {
        await page.goto(path)
        await expect(page.getByRole('heading', { level: 1 })).toContainText(heading)
        await expectAccessible(page, `${path} (${scheme})`)
      })
    }
  })
}

test('pages are prerendered with SEO tags and hydrate without errors', async ({ page }) => {
  const errors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text())
  })
  page.on('pageerror', (error) => errors.push(error.message))
  const response = await page.goto('/de/bot')
  const html = await response!.text()
  expect(html).toContain('<html lang="de"')
  expect(html).toMatch(/<link rel="canonical" href="[^"]+\/de\/bot">/)
  expect(html).toMatch(/<link rel="alternate" hreflang="en" href="[^"]+\/bot">/)
  expect(html).toMatch(/<link rel="alternate" hreflang="x-default" href="[^"]+\/bot">/)
  expect(html).toMatch(/<meta property="og:image" content="[^"]+\/og\/de\.png">/)
  await expect(page.getByTestId('bot-robots')).toHaveText('User-agent: sCalenderPlusBot\nDisallow: /')
  await page.waitForLoadState('networkidle')
  expect(errors).toEqual([])
})

test('the demo and the pricing period switch after hydration', async ({ page }) => {
  await page.goto('/')
  const demo = page.getByTestId('access-demo')
  await expect(demo.getByTestId('demo-event')).toHaveCount(4)
  await demo.getByRole('button', { name: 'Public link' }).click()
  await expect(demo.getByTestId('demo-event')).toHaveCount(2)
  await expect(demo.getByTestId('demo-hidden')).toHaveText('2 entries hidden')

  const pro = page.getByTestId('plan-pro').getByTestId('plan-price')
  await expect(pro).toHaveText('€12')
  await page.getByTestId('period-yearly').click()
  await expect(pro).toHaveText('€120')
})

test('footer links the legal pages and the bot page; language switch keeps the page', async ({
  page,
}) => {
  await page.goto('/legal/privacy')
  const footer = page.getByTestId('site-footer')
  await footer.getByRole('link', { name: 'Importer bot' }).click()
  await expect(page).toHaveURL(/\/bot$/)
  await page.getByTestId('language-switch').click()
  await expect(page).toHaveURL(/\/de\/bot$/)
  await expect(page.locator('html')).toHaveAttribute('lang', 'de')
  await footer.getByRole('link', { name: 'Impressum' }).click()
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Impressum')
  await expect(page.getByTestId('legal-template')).toBeVisible()
})

test('the placeholder DPA can be downloaded', async ({ page }) => {
  await page.goto('/legal/dpa')
  const response = await page.request.get(
    (await page.getByTestId('dpa-download').getAttribute('href'))!,
  )
  expect(response.ok()).toBe(true)
  expect((await response.body()).subarray(0, 5).toString()).toBe('%PDF-')
})
