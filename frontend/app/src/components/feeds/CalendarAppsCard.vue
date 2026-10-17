<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { CalendarPlus } from '@lucide/vue'
import { UiButton } from '@scalenderplus/ui'
import SettingsCard from '@/components/SettingsCard.vue'
import TextLink from '@/components/TextLink.vue'
import SubscribeCalendarDialog from '@/components/feeds/SubscribeCalendarDialog.vue'

/** Calendar settings: subscribe to this calendar in a calendar app (a personal feed link). */
defineProps<{ calendarId: string; calendarName: string }>()

const { t } = useI18n()
const open = ref(false)
</script>

<template>
  <SettingsCard
    :title="t('feeds.subscribe.cardTitle')"
    :description="t('feeds.subscribe.cardDescription')"
  >
    <div class="flex flex-col items-start gap-3 text-sm">
      <UiButton variant="secondary" data-testid="calendar-subscribe" @click="open = true">
        <CalendarPlus class="size-4" aria-hidden="true" />{{ t('feeds.subscribe.action') }}
      </UiButton>
      <p class="flex flex-wrap gap-x-4 gap-y-1">
        <TextLink :to="{ name: 'settings-feeds' }">{{ t('feeds.subscribe.allLinks') }}</TextLink>
        <TextLink :to="{ name: 'help-calendar-apps' }">{{ t('feeds.help') }}</TextLink>
      </p>
    </div>
  </SettingsCard>
  <SubscribeCalendarDialog
    v-model:open="open"
    :calendar-id="calendarId"
    :calendar-name="calendarName"
  />
</template>
