<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Info, RefreshCw } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import { useBrand } from '@/config'

/**
 * `/help/calendar-apps`: how to subscribe to a feed in Apple Calendar, Google Calendar, Outlook and
 * Thunderbird, with each app's refresh behaviour (docs/guides/calendar-apps.md has the same
 * content; refresh facts from docs/architecture/sync.md). The feed UI links here.
 */
const { t } = useI18n()
const app = useBrand().name

const apps = [
  { id: 'apple', items: ['mac', 'ios'] },
  { id: 'google', items: ['web', 'mobile'] },
  { id: 'outlook', items: ['web', 'classic', 'mobile'] },
  { id: 'thunderbird', items: ['steps'] },
] as const
const before = ['link', 'private', 'readOnly'] as const
const caveats = ['delay', 'recall', 'busy', 'rate'] as const
const key = (path: string) => `help.calendarApps.${path}`
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-8" data-testid="help-calendar-apps">
    <PageHeader :title="t(key('title'))" :description="t(key('intro'), { app })" />

    <section class="flex flex-col gap-3" aria-labelledby="help-before">
      <h2 id="help-before" class="text-lg font-semibold">{{ t(key('before.title')) }}</h2>
      <ul class="flex list-disc flex-col gap-2 pl-5 text-sm leading-relaxed">
        <li v-for="item in before" :key="item">{{ t(key(`before.${item}`), { app }) }}</li>
      </ul>
    </section>

    <section
      v-for="entry in apps"
      :key="entry.id"
      class="flex flex-col gap-3 rounded-2xl border border-border bg-surface-raised p-5"
      :aria-labelledby="`help-${entry.id}`"
      :data-testid="`help-${entry.id}`"
    >
      <h2 :id="`help-${entry.id}`" class="text-lg font-semibold">
        {{ t(key(`${entry.id}.title`)) }}
      </h2>
      <p v-for="item in entry.items" :key="item" class="text-sm leading-relaxed">
        {{ t(key(`${entry.id}.${item}`), { app }) }}
      </p>
      <p
        class="flex items-start gap-2 rounded-lg bg-surface-muted px-3 py-2 text-sm text-on-surface-muted"
      >
        <RefreshCw class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>{{ t(key(`${entry.id}.refresh`), { app }) }}</span>
      </p>
    </section>

    <section class="flex flex-col gap-3" aria-labelledby="help-caveats">
      <h2 id="help-caveats" class="flex items-center gap-2 text-lg font-semibold">
        <Info class="size-5 text-primary" aria-hidden="true" />
        {{ t(key('caveats.title')) }}
      </h2>
      <ul class="flex list-disc flex-col gap-2 pl-5 text-sm leading-relaxed">
        <li v-for="item in caveats" :key="item">{{ t(key(`caveats.${item}`), { app }) }}</li>
      </ul>
    </section>
  </div>
</template>
