<script setup lang="ts">
import {
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
  DialogTrigger,
} from 'reka-ui'

withDefaults(
  defineProps<{
    title: string
    description?: string
    /** Label of the close button (icon only); pass a translated text. */
    closeLabel?: string
    size?: 'sm' | 'md' | 'lg'
    testid?: string
  }>(),
  { closeLabel: 'Close', size: 'md' },
)
const open = defineModel<boolean>('open', { default: false })

const widths = { sm: 'max-w-sm', md: 'max-w-md', lg: 'max-w-lg' } as const
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogTrigger v-if="$slots.trigger" as-child>
      <slot name="trigger" />
    </DialogTrigger>
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 z-40 animate-fade-in bg-overlay" />
      <DialogContent
        :data-testid="testid"
        class="fixed top-1/2 left-1/2 z-50 flex max-h-[calc(100dvh-2rem)] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 animate-scale-in flex-col overflow-hidden rounded-2xl border border-border bg-surface-raised text-on-surface shadow-overlay focus:outline-none"
        :class="widths[size]"
        v-bind="description ? {} : { 'aria-describedby': undefined }"
      >
        <div class="flex items-start justify-between gap-4 px-6 pt-5">
          <div class="flex flex-col gap-1">
            <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
            <DialogDescription v-if="description" class="text-sm text-on-surface-muted">
              {{ description }}
            </DialogDescription>
          </div>
          <DialogClose
            class="-mt-1 -mr-2 inline-flex size-9 shrink-0 items-center justify-center rounded-lg text-on-surface-muted hover:bg-surface-hover hover:text-on-surface focus-visible:outline-2 focus-visible:outline-focus"
            :aria-label="closeLabel"
          >
            <svg viewBox="0 0 20 20" fill="currentColor" class="size-5" aria-hidden="true">
              <path
                d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"
              />
            </svg>
          </DialogClose>
        </div>
        <div class="overflow-y-auto px-6 pt-4 pb-6">
          <slot />
        </div>
        <div
          v-if="$slots.footer"
          class="flex flex-col-reverse gap-2 border-t border-border bg-surface-muted px-6 py-4 sm:flex-row sm:justify-end"
        >
          <slot name="footer" />
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
