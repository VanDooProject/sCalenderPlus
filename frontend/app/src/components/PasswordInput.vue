<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Eye, EyeOff } from '@lucide/vue'
import { UiInput } from '@scalenderplus/ui'

defineOptions({ inheritAttrs: false })
const model = defineModel<string>({ default: '' })
const { t } = useI18n()
const visible = ref(false)
</script>

<template>
  <UiInput v-model="model" v-bind="$attrs" :type="visible ? 'text' : 'password'">
    <template #trailing>
      <button
        type="button"
        class="inline-flex size-8 items-center justify-center rounded-md text-on-surface-muted hover:bg-surface-hover hover:text-on-surface focus-visible:outline-2 focus-visible:outline-focus"
        :aria-label="visible ? t('auth.hidePassword') : t('auth.showPassword')"
        :aria-pressed="visible"
        @click="visible = !visible"
      >
        <EyeOff v-if="visible" class="size-4.5" aria-hidden="true" />
        <Eye v-else class="size-4.5" aria-hidden="true" />
      </button>
    </template>
  </UiInput>
</template>
