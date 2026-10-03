// Conventional Commits rules (docs/development/workflow.md §1). Because PRs are squash-merged, CI
// validates the PR title (`pr-checks.yml`, amannn/action-semantic-pull-request), reading `types` and
// `scopes` from this file. Locally usable with commitlint (`@commitlint/config-conventional`).

/** Allowed commit types (same as @commitlint/config-conventional). */
export const types = [
  'feat',
  'fix',
  'perf',
  'refactor',
  'docs',
  'style',
  'test',
  'build',
  'ci',
  'chore',
  'revert',
]

/** Allowed scopes (optional in a commit, but must be one of these when given). */
export const scopes = [
  'api',
  'core',
  'worker',
  'db',
  'auth',
  'perm',
  'ical',
  'caldav',
  'import',
  'billing',
  'web',
  'landing',
  'ui',
  'e2e',
  'deploy',
  'ci',
  'docs',
  'deps',
]

export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [2, 'always', types],
    'scope-enum': [2, 'always', scopes],
  },
}
