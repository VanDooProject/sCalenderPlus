<script setup lang="ts">
import { ref } from 'vue'
import { useFieldControl } from '../composables/field'

defineOptions({ inheritAttrs: false })

const props = defineProps<{
  id?: string
  invalid?: boolean
}>()
const model = defineModel<string>({ default: '' })
const { id, describedBy, invalid, required } = useFieldControl(props)

const input = ref<HTMLInputElement>()
defineExpose({ focus: () => input.value?.focus() })
</script>

<template>
  <div
    class="group flex h-10 w-full items-center rounded-lg border bg-surface-raised text-on-surface shadow-xs transition-colors focus-within:outline-2 focus-within:-outline-offset-1 focus-within:outline-focus"
    :class="invalid ? 'border-danger' : 'border-border-strong hover:border-on-surface-muted'"
  >
    <span v-if="$slots.leading" class="pl-3 text-on-surface-muted">
      <slot name="leading" />
    </span>
    <input
      :id="id"
      ref="input"
      v-model="model"
      v-bind="$attrs"
      :aria-describedby="describedBy"
      :aria-invalid="invalid || undefined"
      :required="required || undefined"
      class="h-full w-full min-w-0 flex-1 rounded-lg bg-transparent px-3 text-base placeholder:text-on-surface-muted focus:outline-none sm:text-sm"
    />
    <span v-if="$slots.trailing" class="flex pr-1.5">
      <slot name="trailing" />
    </span>
  </div>
</template>
