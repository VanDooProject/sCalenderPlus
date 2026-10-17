<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Upload } from '@lucide/vue'
import { UiButton } from '@scalenderplus/ui'
import type { CalendarResponse } from '@scalenderplus/api-client'
import SettingsCard from '@/components/SettingsCard.vue'
import IcsImportDialog from './IcsImportDialog.vue'

/** "Import events" in the calendar settings: opens the ICS upload (contribute or more). */
defineProps<{ calendar: CalendarResponse }>()

const { t } = useI18n()
const open = ref(false)
</script>

<template>
  <SettingsCard :title="t('icsImport.cardTitle')" :description="t('icsImport.cardDescription')">
    <div class="flex flex-col items-start gap-3">
      <p
        v-if="calendar.frozen"
        class="text-sm text-on-surface-muted"
        data-testid="ics-import-frozen"
      >
        {{ t('icsImport.frozen') }}
      </p>
      <UiButton
        variant="secondary"
        :disabled="calendar.frozen"
        data-testid="ics-import-open"
        @click="open = true"
      >
        <Upload class="size-4" aria-hidden="true" />{{ t('icsImport.open') }}
      </UiButton>
    </div>
    <IcsImportDialog v-model:open="open" :calendar="calendar" />
  </SettingsCard>
</template>
