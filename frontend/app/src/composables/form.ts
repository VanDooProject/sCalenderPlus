import { nextTick, reactive, ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError } from '@/lib/apiError'
import { errorMessage } from '@/lib/errorMessages'

function omit<V>(record: Record<string, V>, key: string): Record<string, V> {
  return Object.fromEntries(Object.entries(record).filter(([k]) => k !== key))
}

export type FieldErrors<T> = Partial<Record<keyof T & string, string>>

export interface FormOptions<T extends object> {
  /** Client-side checks with translated messages; the api re-validates everything. */
  validate?: (values: T) => FieldErrors<T>
  /** Form element; the first invalid control gets the focus after a failed submit. */
  formRef?: Ref<HTMLFormElement | undefined>
}

/**
 * Small form state for the auth and settings forms: values, client and server field errors
 * (problem `errors` keyed by the camelCase member path), a form-level message and the pending flag.
 * Field errors clear as soon as the field changes.
 */
export function useForm<T extends object>(initial: T, options: FormOptions<T> = {}) {
  const { t } = useI18n()
  const values = reactive({ ...initial }) as T
  const clientErrors = ref<Record<string, string>>({})
  const serverErrors = ref<Record<string, string[]>>({})
  const formError = ref<string | null>(null)
  const pending = ref(false)
  const submitted = ref(false)

  for (const key of Object.keys(initial)) {
    watch(
      () => (values as Record<string, unknown>)[key],
      () => {
        if (key in clientErrors.value || key in serverErrors.value) {
          clientErrors.value = omit(clientErrors.value, key)
          serverErrors.value = omit(serverErrors.value, key)
        }
      },
    )
  }

  function errors(field: keyof T & string): string[] {
    const client = clientErrors.value[field]
    return client ? [client] : (serverErrors.value[field] ?? [])
  }

  async function focusFirstInvalid() {
    await nextTick()
    const form = options.formRef?.value
    const target = form?.querySelector<HTMLElement>('[aria-invalid="true"]')
    target?.focus()
  }

  /**
   * Validates, then runs `action`. Problem responses become field errors (known fields) and a
   * form-level message; returns whether the action succeeded.
   */
  async function submit(action: (values: T) => Promise<void>): Promise<boolean> {
    submitted.value = true
    formError.value = null
    serverErrors.value = {}
    const found = Object.fromEntries(
      Object.entries(options.validate?.(values) ?? {}).filter(([, message]) => !!message),
    ) as Record<string, string>
    clientErrors.value = found
    if (Object.keys(found).length > 0) {
      await focusFirstInvalid()
      return false
    }
    pending.value = true
    try {
      await action(values)
      return true
    } catch (error) {
      handleError(error)
      return false
    } finally {
      pending.value = false
    }
  }

  function handleError(error: unknown) {
    if (error instanceof ApiError) {
      const fields = error.fieldErrors
      const known = Object.keys(initial)
      serverErrors.value = Object.fromEntries(
        Object.entries(fields).filter(([key]) => known.includes(key)),
      )
      const unknown = Object.entries(fields)
        .filter(([key]) => !known.includes(key))
        .flatMap(([, messages]) => messages)
      const hasFieldErrors = Object.keys(serverErrors.value).length > 0
      formError.value = hasFieldErrors
        ? [t('errors.checkFields'), ...unknown].join(' ')
        : [errorMessage(t, error), ...unknown].join(' ')
      if (hasFieldErrors) void focusFirstInvalid()
      return
    }
    formError.value = errorMessage(t, error)
  }

  function reset(next: T = initial) {
    Object.assign(values, next)
    clientErrors.value = {}
    serverErrors.value = {}
    formError.value = null
    submitted.value = false
  }

  return { values, errors, formError, pending, submitted, submit, handleError, reset }
}

/** Shared client-side rules (messages from `validation.*`). */
export function useValidators() {
  const { t } = useI18n()
  return {
    required: (value: string) => (value.trim() === '' ? t('validation.required') : undefined),
    email: (value: string) =>
      value.trim() === ''
        ? t('validation.required')
        : /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim())
          ? undefined
          : t('validation.email'),
    password: (value: string) =>
      value === ''
        ? t('validation.required')
        : value.length < 10
          ? t('validation.passwordLength', { min: 10 })
          : undefined,
    code: (value: string) =>
      /^\d{6}$/.test(value.replace(/\s/g, '')) ? undefined : t('validation.code'),
  }
}
