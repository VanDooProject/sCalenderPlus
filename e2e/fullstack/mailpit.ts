import { expect, type APIRequestContext } from '@playwright/test'

/** Mailpit of the compose stack (`--profile with-mailpit`): its HTTP API on 127.0.0.1:8025. */
export const mailpitUrl = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'

interface MailpitSummary {
  ID: string
  Subject: string
}

/**
 * Waits for the newest email to `to` whose subject matches and returns its text body. Emails are
 * sent by the worker after the request's transaction commits, so they arrive with a short delay.
 */
export async function waitForEmail(
  request: APIRequestContext,
  to: string,
  subject: RegExp,
  timeout = 30_000,
): Promise<string> {
  let id: string | undefined
  await expect
    .poll(
      async () => {
        const search = await request.get(`${mailpitUrl}/api/v1/search`, {
          params: { query: `to:"${to}"` },
        })
        if (!search.ok()) return undefined
        const { messages } = (await search.json()) as { messages: MailpitSummary[] }
        id = messages.find((m) => subject.test(m.Subject))?.ID
        return id
      },
      { timeout, message: `email to ${to} matching ${subject}` },
    )
    .toBeTruthy()
  const message = await request.get(`${mailpitUrl}/api/v1/message/${id}`)
  expect(message.ok()).toBe(true)
  return ((await message.json()) as { Text: string }).Text
}

/** The first link to `path` of the app in an email body, as a path + query for `page.goto`. */
export function linkTo(body: string, path: string): string {
  const match = body.match(new RegExp(`https?://[^\\s"'<>]+${path}\\?[^\\s"'<>]+`))
  expect(match, `link to ${path} in:\n${body}`).not.toBeNull()
  const url = new URL(match![0])
  return `${url.pathname}${url.search}`
}
