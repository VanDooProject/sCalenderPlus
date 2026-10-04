<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Check, Copy, Download } from '@lucide/vue'
import { UiButton } from '@scalenderplus/ui'

/** One-time display of new recovery codes with copy and download. */
const props = defineProps<{ codes: readonly string[] }>()
const { t } = useI18n()
const copied = ref(false)

const text = () => `${t('settings.security.recoveryFileHeader')}\n\n${props.codes.join('\n')}\n`

async function copy() {
  try {
    await navigator.clipboard.writeText(props.codes.join('\n'))
    copied.value = true
    setTimeout(() => (copied.value = false), 2000)
  } catch {
    copied.value = false
  }
}

function download() {
  const url = URL.createObjectURL(new Blob([text()], { type: 'text/plain' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'scalenderplus-recovery-codes.txt'
  link.click()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <div class="flex flex-col gap-4" data-testid="recovery-codes">
    <ul
      class="grid grid-cols-2 gap-x-4 gap-y-2 rounded-xl border border-border bg-surface-muted p-4 font-mono text-sm tracking-wide"
      :aria-label="t('settings.security.recoveryCodes')"
    >
      <li v-for="code in codes" :key="code" data-testid="recovery-code">{{ code }}</li>
    </ul>
    <div class="flex flex-wrap gap-2">
      <UiButton variant="secondary" size="sm" data-testid="recovery-copy" @click="copy">
        <Check v-if="copied" class="size-4" aria-hidden="true" />
        <Copy v-else class="size-4" aria-hidden="true" />
        {{ copied ? t('common.copied') : t('common.copy') }}
      </UiButton>
      <UiButton variant="secondary" size="sm" data-testid="recovery-download" @click="download">
        <Download class="size-4" aria-hidden="true" />{{ t('common.download') }}
      </UiButton>
      <span class="sr-only" aria-live="polite">{{ copied ? t('common.copied') : '' }}</span>
    </div>
  </div>
</template>
