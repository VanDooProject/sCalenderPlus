<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import { CalendarCheck, CopyCheck, FileUp, Repeat, TriangleAlert } from '@lucide/vue'
import { UiAlert, UiButton, UiDialog, useToast } from '@scalenderplus/ui'
import type { CalendarResponse, IcsUploadItem, IcsUploadResponse } from '@scalenderplus/api-client'
import FormAlert from '@/components/FormAlert.vue'
import { errorMessage } from '@/lib/errorMessages'
import {
  formatItemStart,
  maxIcsBytes,
  reasonKey,
  uploadIcs,
  warningKey,
  type IcsAction,
} from '@/lib/icsImport'

/**
 * One-time import of an `.ics` file (`POST /calendars/{id}/ics-upload`, ical-caldav.md §2): pick a
 * file, see what a dry run would do (counts, warnings, every event with its action), then import.
 * The commit sends the same file again; events whose UID the calendar has are left out, so
 * importing the same export twice adds nothing.
 */
const props = defineProps<{ calendar: CalendarResponse }>()
const open = defineModel<boolean>('open', { default: false })

const { t, locale } = useI18n()
const toast = useToast()
const queryClient = useQueryClient()

/** How many items the preview lists (the counts cover all of them). */
const listLimit = 200

const fileInput = ref<HTMLInputElement>()
const file = ref<{ name: string; text: string } | null>(null)
const preview = ref<IcsUploadResponse | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)

watch(open, (value) => {
  if (!value) return
  file.value = null
  preview.value = null
  error.value = null
})

const counts = computed(() => preview.value?.counts)
const shown = computed(() => preview.value?.items.slice(0, listLimit) ?? [])
const hidden = computed(() => Math.max(0, (preview.value?.items.length ?? 0) - listLimit))
const warnings = computed(() =>
  (preview.value?.warnings ?? [])
    .map((w) => ({ ...w, key: warningKey(w.code) }))
    .filter((w): w is { code: string; count: number; key: string } => w.key !== null),
)

async function choose(event: Event) {
  const input = event.target as HTMLInputElement
  const picked = input.files?.[0]
  input.value = '' // the same file can be picked again
  if (!picked) return
  error.value = null
  preview.value = null
  file.value = null
  if (picked.size > maxIcsBytes) {
    error.value = t('icsImport.tooLarge')
    return
  }
  const text = await picked.text()
  file.value = { name: picked.name, text }
  await run(true)
}

async function run(dryRun: boolean) {
  if (!file.value) return
  busy.value = true
  error.value = null
  try {
    const report = await uploadIcs(props.calendar.id, file.value.text, dryRun)
    if (dryRun) {
      preview.value = report
      return
    }
    toast.success(t('icsImport.done', report.counts.created))
    open.value = false
    await queryClient.invalidateQueries({ queryKey: ['events'] })
  } catch (e) {
    error.value = errorMessage(t, e)
  } finally {
    busy.value = false
  }
}

function badge(action: string): { label: string; tone: string } {
  const tones: Record<IcsAction, string> = {
    create: 'bg-success-soft text-on-success-soft',
    duplicate: 'bg-surface-hover text-on-surface-muted',
    skip: 'bg-warning-soft text-on-warning-soft',
  }
  const known = (['create', 'duplicate', 'skip'] as const).find((a) => a === action) ?? 'skip'
  return { label: t(`icsImport.actions.${known}`), tone: tones[known] }
}

function detail(item: IcsUploadItem): string {
  const parts = [formatItemStart(item, locale.value)]
  if (item.rrule) parts.push(t('icsImport.recurring'))
  if (item.modifiedOccurrences > 0) parts.push(t('icsImport.modified', item.modifiedOccurrences))
  if (item.cancelledOccurrences > 0) parts.push(t('icsImport.cancelled', item.cancelledOccurrences))
  const reason = reasonKey(item.reason)
  if (item.action !== 'create' && reason) parts.push(t(reason))
  return parts.filter(Boolean).join(' · ')
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="t('icsImport.title', { name: calendar.name })"
    :description="t('icsImport.description')"
    :close-label="t('common.close')"
    size="lg"
    testid="ics-import-dialog"
  >
    <div class="flex flex-col gap-4">
      <FormAlert :message="error" />

      <div
        class="flex flex-col gap-3 rounded-xl border border-dashed border-border-strong p-4 sm:flex-row sm:items-center sm:justify-between"
      >
        <div class="flex min-w-0 items-center gap-3">
          <FileUp class="size-5 shrink-0 text-on-surface-muted" aria-hidden="true" />
          <p class="min-w-0 text-sm break-words" data-testid="ics-import-file">
            {{ file?.name ?? t('icsImport.noFile') }}
          </p>
        </div>
        <UiButton
          variant="secondary"
          size="sm"
          :disabled="busy"
          data-testid="ics-import-choose"
          @click="fileInput?.click()"
        >
          {{ file ? t('icsImport.chooseAnother') : t('icsImport.choose') }}
        </UiButton>
        <input
          ref="fileInput"
          type="file"
          accept=".ics,.ical,.ifb,.icalendar,text/calendar"
          class="sr-only"
          tabindex="-1"
          :aria-label="t('icsImport.choose')"
          data-testid="ics-import-input"
          @change="choose"
        />
      </div>
      <p v-if="!preview" class="text-sm text-on-surface-muted">{{ t('icsImport.hint') }}</p>

      <template v-if="preview && counts">
        <ul class="grid gap-2 sm:grid-cols-3" data-testid="ics-import-counts">
          <li class="flex items-center gap-2 rounded-xl bg-surface-muted p-3 text-sm">
            <CalendarCheck class="size-4 shrink-0 text-on-success-soft" aria-hidden="true" />
            <span data-testid="ics-import-created">{{
              t('icsImport.counts.created', counts.created)
            }}</span>
          </li>
          <li class="flex items-center gap-2 rounded-xl bg-surface-muted p-3 text-sm">
            <CopyCheck class="size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
            <span data-testid="ics-import-duplicates">{{
              t('icsImport.counts.duplicates', counts.duplicates)
            }}</span>
          </li>
          <li class="flex items-center gap-2 rounded-xl bg-surface-muted p-3 text-sm">
            <TriangleAlert class="size-4 shrink-0 text-on-warning-soft" aria-hidden="true" />
            <span data-testid="ics-import-skipped">{{
              t('icsImport.counts.skipped', counts.skipped)
            }}</span>
          </li>
        </ul>
        <p v-if="counts.series > 0" class="flex items-center gap-2 text-sm text-on-surface-muted">
          <Repeat class="size-4 shrink-0" aria-hidden="true" />
          {{
            t('icsImport.counts.series', {
              series: t('icsImport.counts.seriesCount', counts.series),
              modified: t('icsImport.modified', counts.modifiedOccurrences),
              cancelled: t('icsImport.cancelled', counts.cancelledOccurrences),
            })
          }}
        </p>
        <UiAlert v-if="warnings.length" variant="warning" data-testid="ics-import-warnings">
          <ul class="flex list-disc flex-col gap-1 pl-4">
            <li v-for="w in warnings" :key="w.code" :data-testid="`ics-warning-${w.code}`">
              {{ t(w.key, { count: w.count }, w.count) }}
            </li>
          </ul>
        </UiAlert>
        <UiAlert v-if="counts.created === 0" data-testid="ics-import-nothing">
          {{ t('icsImport.nothing') }}
        </UiAlert>

        <ul
          class="flex max-h-72 flex-col divide-y divide-border overflow-y-auto rounded-xl border border-border"
          :aria-label="t('icsImport.listLabel')"
          data-testid="ics-import-items"
        >
          <li
            v-for="(item, index) in shown"
            :key="`${item.uid}-${index}`"
            class="flex items-start justify-between gap-3 px-3 py-2"
            data-testid="ics-import-item"
          >
            <div class="flex min-w-0 flex-col">
              <span class="text-sm font-medium break-words">{{ item.title }}</span>
              <span class="text-xs text-on-surface-muted">{{ detail(item) }}</span>
            </div>
            <span
              class="shrink-0 rounded-full px-2 py-0.5 text-xs font-medium"
              :class="badge(item.action).tone"
            >
              {{ badge(item.action).label }}
            </span>
          </li>
          <li v-if="hidden > 0" class="px-3 py-2 text-xs text-on-surface-muted">
            {{ t('icsImport.more', hidden) }}
          </li>
        </ul>
      </template>
    </div>
    <template #footer>
      <UiButton variant="ghost" @click="open = false">{{ t('common.cancel') }}</UiButton>
      <UiButton
        :disabled="!preview || !counts || counts.created === 0"
        :loading="busy"
        data-testid="ics-import-submit"
        @click="run(false)"
      >
        {{ t('icsImport.submit', counts?.created ?? 0) }}
      </UiButton>
    </template>
  </UiDialog>
</template>
