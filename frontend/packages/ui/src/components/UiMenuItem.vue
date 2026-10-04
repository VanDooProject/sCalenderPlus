<script setup lang="ts">
import { DropdownMenuItem } from 'reka-ui'

defineProps<{
  /** Render the child (e.g. a RouterLink) as the item. */
  asChild?: boolean
  danger?: boolean
  disabled?: boolean
}>()
defineEmits<{ select: [event: Event] }>()
</script>

<template>
  <DropdownMenuItem
    :as-child="asChild"
    :disabled="disabled"
    class="flex h-9 w-full cursor-default items-center gap-2.5 rounded-lg px-2.5 text-sm outline-none select-none data-[disabled]:opacity-50 data-[highlighted]:bg-surface-hover [&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-on-surface-muted"
    :class="danger ? 'text-on-danger-soft data-[highlighted]:bg-danger-soft' : ''"
    @select="$emit('select', $event)"
  >
    <slot />
  </DropdownMenuItem>
</template>
