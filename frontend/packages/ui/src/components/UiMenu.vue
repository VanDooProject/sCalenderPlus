<script setup lang="ts">
import {
  DropdownMenuContent,
  DropdownMenuPortal,
  DropdownMenuRoot,
  DropdownMenuTrigger,
} from 'reka-ui'

/** Dropdown menu: the `trigger` slot is the button (rendered as child), the default slot the items. */
withDefaults(
  defineProps<{
    align?: 'start' | 'center' | 'end'
    testid?: string
  }>(),
  { align: 'end' },
)
const open = defineModel<boolean>('open', { default: false })
</script>

<template>
  <DropdownMenuRoot v-model:open="open" :modal="false">
    <DropdownMenuTrigger as-child>
      <slot name="trigger" />
    </DropdownMenuTrigger>
    <DropdownMenuPortal>
      <DropdownMenuContent
        :align="align"
        :side-offset="6"
        :data-testid="testid"
        class="z-50 min-w-56 animate-scale-in rounded-xl border border-border bg-surface-raised p-1 text-on-surface shadow-overlay"
      >
        <slot />
      </DropdownMenuContent>
    </DropdownMenuPortal>
  </DropdownMenuRoot>
</template>
