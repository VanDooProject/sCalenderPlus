<script setup lang="ts">
import {
  DialogClose,
  DialogContent,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
} from 'reka-ui'

/** Modal side panel (mobile navigation drawer); slides in from the left. */
withDefaults(
  defineProps<{
    /** Accessible name of the panel (visually hidden). */
    title: string
    closeLabel?: string
    testid?: string
  }>(),
  { closeLabel: 'Close' },
)
const open = defineModel<boolean>('open', { default: false })
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 z-40 animate-fade-in bg-overlay" />
      <DialogContent
        :data-testid="testid"
        :aria-describedby="undefined"
        class="fixed inset-y-0 left-0 z-50 flex w-[min(20rem,85vw)] animate-slide-in-left flex-col border-r border-border bg-surface-muted text-on-surface shadow-overlay focus:outline-none"
      >
        <DialogTitle class="sr-only">{{ title }}</DialogTitle>
        <DialogClose
          class="absolute top-3.5 right-3 inline-flex size-9 items-center justify-center rounded-lg text-on-surface-muted hover:bg-surface-hover hover:text-on-surface focus-visible:outline-2 focus-visible:outline-focus"
          :aria-label="closeLabel"
        >
          <svg viewBox="0 0 20 20" fill="currentColor" class="size-5" aria-hidden="true">
            <path
              d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"
            />
          </svg>
        </DialogClose>
        <slot />
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
