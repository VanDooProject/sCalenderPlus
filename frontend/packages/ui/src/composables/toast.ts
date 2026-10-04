import { readonly, ref } from 'vue'

export type ToastVariant = 'success' | 'error' | 'info'

export interface ToastOptions {
  title: string
  description?: string
  variant?: ToastVariant
  /** Milliseconds until it closes; errors stay longer by default. */
  duration?: number
}

export interface ToastItem extends Required<Omit<ToastOptions, 'description'>> {
  id: number
  description?: string
  open: boolean
}

const items = ref<ToastItem[]>([])
let nextId = 1

/** Most toasts at once; older ones close first. */
const maxToasts = 4

function show(options: ToastOptions): number {
  const variant = options.variant ?? 'info'
  const id = nextId++
  items.value.push({
    id,
    title: options.title,
    description: options.description,
    variant,
    duration: options.duration ?? (variant === 'error' ? 8000 : 5000),
    open: true,
  })
  const open = items.value.filter((t) => t.open)
  if (open.length > maxToasts) {
    open.slice(0, open.length - maxToasts).forEach((t) => (t.open = false))
  }
  return id
}

function dismiss(id: number): void {
  const item = items.value.find((t) => t.id === id)
  if (item) item.open = false
}

/** Drops a closed toast once its exit animation has run (called by {@link UiToaster}). */
function remove(id: number): void {
  items.value = items.value.filter((t) => t.id !== id)
}

/**
 * App-wide notification queue rendered by one `<UiToaster />`. Module state: usable outside
 * components too (query error handlers, router guards).
 */
export function useToast() {
  return {
    toasts: readonly(items),
    show,
    success: (title: string, description?: string) =>
      show({ title, description, variant: 'success' }),
    error: (title: string, description?: string) => show({ title, description, variant: 'error' }),
    info: (title: string, description?: string) => show({ title, description, variant: 'info' }),
    dismiss,
    remove,
    clear: () => {
      items.value = []
    },
  }
}
