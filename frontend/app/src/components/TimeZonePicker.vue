<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  ComboboxAnchor,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxItemIndicator,
  ComboboxPortal,
  ComboboxRoot,
  ComboboxTrigger,
  ComboboxViewport,
} from 'reka-ui'
import { Check, ChevronsUpDown, Search } from '@lucide/vue'
import { useFieldControl } from '@scalenderplus/ui'
import { availableTimeZones, filterTimeZones, timeZoneOffset } from '@/lib/timeZones'

/** Searchable IANA time zone picker (combobox). */
const props = defineProps<{ id?: string; invalid?: boolean }>()
const model = defineModel<string>({ required: true })
const { t } = useI18n()
const { id, describedBy, invalid } = useFieldControl(props)

const searchTerm = ref('')
const zones = computed(() => {
  const all = availableTimeZones()
  // Keep a stored zone selectable even if this browser does not list it.
  return all.includes(model.value) ? all : [model.value, ...all]
})
const results = computed(() => filterTimeZones(zones.value, searchTerm.value))
const label = (zone: string) => zone.replaceAll('_', ' ')
</script>

<template>
  <ComboboxRoot
    v-model="model"
    ignore-filter
    reset-search-term-on-blur
    reset-search-term-on-select
    open-on-click
  >
    <ComboboxAnchor
      class="flex h-10 w-full items-center rounded-lg border bg-surface-raised text-on-surface shadow-xs transition-colors focus-within:outline-2 focus-within:-outline-offset-1 focus-within:outline-focus"
      :class="invalid ? 'border-danger' : 'border-border-strong hover:border-on-surface-muted'"
    >
      <Search class="ml-3 size-4 shrink-0 text-on-surface-muted" aria-hidden="true" />
      <ComboboxInput
        :id="id"
        v-model="searchTerm"
        :display-value="(value: string) => label(value ?? '')"
        :placeholder="t('settings.profile.timeZoneSearch')"
        :aria-describedby="describedBy"
        :aria-invalid="invalid || undefined"
        class="h-full w-full min-w-0 flex-1 bg-transparent px-2.5 text-base placeholder:text-on-surface-muted focus:outline-none sm:text-sm"
        data-testid="time-zone-input"
      />
      <ComboboxTrigger
        class="mr-1.5 inline-flex size-8 items-center justify-center rounded-md text-on-surface-muted hover:bg-surface-hover"
        :aria-label="t('settings.profile.timeZoneShowAll')"
      >
        <ChevronsUpDown class="size-4" aria-hidden="true" />
      </ComboboxTrigger>
    </ComboboxAnchor>
    <ComboboxPortal>
      <ComboboxContent
        position="popper"
        :side-offset="4"
        class="z-50 max-h-72 w-(--reka-combobox-trigger-width) animate-scale-in overflow-hidden rounded-lg border border-border bg-surface-raised text-on-surface shadow-overlay"
      >
        <ComboboxViewport class="max-h-72 overflow-y-auto p-1">
          <ComboboxEmpty class="px-3 py-2 text-sm text-on-surface-muted">
            {{ t('settings.profile.timeZoneNone') }}
          </ComboboxEmpty>
          <ComboboxItem
            v-for="zone in results"
            :key="zone"
            :value="zone"
            :text-value="label(zone)"
            class="relative flex h-9 cursor-default items-center justify-between gap-3 rounded-md pr-3 pl-8 text-sm outline-none select-none data-[highlighted]:bg-surface-hover"
            :data-testid="`time-zone-option-${zone}`"
          >
            <ComboboxItemIndicator class="absolute left-2 inline-flex text-primary">
              <Check class="size-4" aria-hidden="true" />
            </ComboboxItemIndicator>
            <span class="truncate">{{ label(zone) }}</span>
            <span class="shrink-0 text-xs text-on-surface-muted tabular-nums">
              {{ timeZoneOffset(zone) }}
            </span>
          </ComboboxItem>
        </ComboboxViewport>
      </ComboboxContent>
    </ComboboxPortal>
  </ComboboxRoot>
</template>
