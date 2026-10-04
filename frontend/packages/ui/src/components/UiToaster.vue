<script setup lang="ts">
import {
  ToastClose,
  ToastDescription,
  ToastProvider,
  ToastRoot,
  ToastTitle,
  ToastViewport,
} from 'reka-ui'
import { useToast } from '../composables/toast'

/** Renders the queue of {@link useToast}; mount once near the app root. */
withDefaults(
  defineProps<{
    /** Accessible label of the region; `{hotkey}` is replaced by the shortcut. */
    label?: string
    closeLabel?: string
  }>(),
  { label: 'Notifications ({hotkey})', closeLabel: 'Close' },
)

const { toasts, dismiss, remove } = useToast()

const accents = {
  success: 'bg-on-success-soft',
  error: 'bg-danger',
  info: 'bg-primary',
} as const

function onOpenChange(id: number, open: boolean) {
  if (!open) {
    dismiss(id)
    // Leave time for the exit animation before dropping it from the list.
    setTimeout(() => remove(id), 200)
  }
}
</script>

<template>
  <ToastProvider :label="label" swipe-direction="right">
    <ToastRoot
      v-for="toast in toasts"
      :key="toast.id"
      :open="toast.open"
      :duration="toast.duration"
      :type="toast.variant === 'error' ? 'foreground' : 'background'"
      data-testid="toast"
      :data-variant="toast.variant"
      class="relative flex animate-slide-in-right items-start gap-3 overflow-hidden rounded-xl border border-border bg-surface-raised py-3 pr-3 pl-4 text-on-surface shadow-overlay data-[state=closed]:opacity-0 data-[state=closed]:transition-opacity data-[swipe=move]:translate-x-(--reka-toast-swipe-move-x)"
      @update:open="onOpenChange(toast.id, $event)"
    >
      <span
        class="absolute inset-y-0 left-0 w-1"
        :class="accents[toast.variant]"
        aria-hidden="true"
      />
      <div class="flex min-w-0 flex-1 flex-col gap-0.5">
        <ToastTitle class="text-sm font-semibold">{{ toast.title }}</ToastTitle>
        <ToastDescription v-if="toast.description" class="text-sm text-on-surface-muted">
          {{ toast.description }}
        </ToastDescription>
      </div>
      <ToastClose
        :aria-label="closeLabel"
        class="inline-flex size-7 shrink-0 items-center justify-center rounded-md text-on-surface-muted hover:bg-surface-hover hover:text-on-surface focus-visible:outline-2 focus-visible:outline-focus"
      >
        <svg viewBox="0 0 20 20" fill="currentColor" class="size-4" aria-hidden="true">
          <path
            d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"
          />
        </svg>
      </ToastClose>
    </ToastRoot>
    <ToastViewport
      class="fixed right-0 bottom-0 z-[60] m-0 flex w-full max-w-sm list-none flex-col gap-2 p-4 outline-none sm:p-6"
    />
  </ToastProvider>
</template>
