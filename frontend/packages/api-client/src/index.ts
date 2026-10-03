/**
 * sCalenderPlus API client: types generated from `backend/openapi/v1.json` by openapi-typescript
 * (`pnpm --filter @scalenderplus/api-client generate`) and a typed openapi-fetch client.
 * See docs/architecture/api.md §6.
 */
import type { components } from './schema'

export { createApiClient, type ApiClient, type ApiClientOptions } from './client'
export type { components, operations, paths } from './schema'

export type HealthResponse = components['schemas']['HealthResponse']

/** The signed-in user (`GET /api/v1/me`). */
export type MeResponse = components['schemas']['MeResponse']

/** JSON Merge Patch body of `PATCH /api/v1/me` (requires `If-Match`). */
export type UpdateProfileRequest = components['schemas']['UpdateProfileRequest']

/** A group as the signed-in member sees it (`GET /api/v1/groups/{id}`, with `ETag`). */
export type GroupResponse = components['schemas']['GroupResponse']

/** A page of `GET /api/v1/groups` (`{ items, nextCursor }`). */
export type GroupListResponse = components['schemas']['GroupListResponse']

/** A member of a group (`GET /api/v1/groups/{id}/members`); `etag` is the `If-Match` for changing it. */
export type MemberResponse = components['schemas']['MemberResponse']

/** A page of `GET /api/v1/groups/{id}/members`. */
export type MemberListResponse = components['schemas']['MemberListResponse']

/** A pending group invite (`GET /api/v1/groups/{id}/invites`); never contains the token. */
export type InviteResponse = components['schemas']['InviteResponse']

/** Body of `POST /api/v1/groups/{id}/invites`: with `email` an email invite, without an invite link. */
export type CreateInviteRequest = components['schemas']['CreateInviteRequest']

/** Result of creating an invite; `url` (links only) contains the token and is shown once. */
export type CreateInviteResponse = components['schemas']['CreateInviteResponse']

/** A group role, lowest to highest: `viewer`, `member`, `admin`, `owner`. */
export type GroupRole = 'viewer' | 'member' | 'admin' | 'owner'

/** Result of `POST /api/v1/auth/login`: a session, or the request for a second factor. */
export type LoginResponse = components['schemas']['LoginResponse']

/** Stable error `code` of a problem response (api.md §2); map to i18n messages, never to `title`. */
export type ErrorCode = components['schemas']['ErrorCode']

/** RFC 9457 problem details body of every error response (`application/problem+json`). */
export type ProblemDetails = components['schemas']['ProblemDetails']

/** Versioned REST base path; same-origin (proxied by `web`). */
export const API_BASE_PATH = '/api/v1'
