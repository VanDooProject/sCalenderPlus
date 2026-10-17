<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Check, PartyPopper, X } from '@lucide/vue'
import { UiButton, useToast } from '@scalenderplus/ui'
import TextLink from '@/components/TextLink.vue'
import CreateCalendarDialog from '@/components/calendar/CreateCalendarDialog.vue'
import CreateFeedDialog from '@/components/feeds/CreateFeedDialog.vue'
import CreateGroupDialog from '@/components/groups/CreateGroupDialog.vue'
import {
  hideOnboarding,
  onboardingFocusRequests,
  onboardingSteps,
  requiredSteps,
  useOnboardingVisibility,
  type OnboardingStep,
} from '@/composables/onboarding'
import { useSession } from '@/composables/session'

/**
 * First-run checklist above every page of the shell (features.md §5 "Onboarding checklist"): create a
 * calendar, invite a group, subscribe in a calendar app and, optionally, try per-event
 * permissions. Each step opens the dialog or page that does it; its tick comes from the account's
 * real state, so doing a step elsewhere ticks it too.
 */
const { t } = useI18n()
const toast = useToast()
const { user } = useSession()
const onboarding = useOnboardingVisibility()
const { done, doneCount, complete, nextStep, managedGroups, visible } = onboarding

const calendarOpen = ref(false)
const groupOpen = ref(false)
const feedOpen = ref(false)

const total = requiredSteps.length
const percent = computed(() => Math.round((doneCount.value / total) * 100))
const verified = computed(() => user.value?.emailVerified ?? false)
/** Where an invite link is made: the newest group the user manages. */
const inviteGroup = computed(() => managedGroups.value[0] ?? null)

const isOptional = (step: OnboardingStep) => !requiredSteps.includes(step)

function hide() {
  const id = onboarding.userId.value
  if (!id) return
  const finished = complete.value
  hideOnboarding(id)
  if (!finished) toast.info(t('onboarding.hidden'))
}

/** Reopened from the user menu: move the focus to the checklist once it shows. */
const heading = ref<HTMLElement>()
const focusPending = ref(false)
watch(onboardingFocusRequests, () => (focusPending.value = true))
watch([focusPending, visible], async ([pending, shown]) => {
  if (!pending || !shown) return
  focusPending.value = false
  await nextTick()
  heading.value?.scrollIntoView({ block: 'nearest' })
  heading.value?.focus()
})
</script>

<template>
  <section
    v-if="visible"
    :aria-label="t('onboarding.title')"
    class="mb-6 rounded-2xl border border-border bg-surface-raised shadow-xs"
    data-testid="onboarding"
  >
    <header class="flex items-start gap-3 border-b border-border px-5 py-4 sm:px-6">
      <div class="flex min-w-0 flex-1 flex-col gap-1">
        <h2
          id="onboarding-title"
          ref="heading"
          tabindex="-1"
          class="text-base font-semibold focus:outline-none"
        >
          {{ complete ? t('onboarding.finishedTitle') : t('onboarding.title') }}
        </h2>
        <p class="text-sm text-on-surface-muted">
          {{ complete ? t('onboarding.finishedText') : t('onboarding.description') }}
        </p>
        <div class="mt-2 flex items-center gap-3">
          <div
            role="progressbar"
            :aria-label="t('onboarding.progressLabel')"
            aria-valuemin="0"
            :aria-valuemax="total"
            :aria-valuenow="doneCount"
            :aria-valuetext="t('onboarding.progress', { done: doneCount, total })"
            class="h-1.5 w-full max-w-48 overflow-hidden rounded-full bg-surface-hover"
          >
            <div
              class="h-full rounded-full bg-primary transition-[width]"
              :style="{ width: `${percent}%` }"
            />
          </div>
          <p
            class="shrink-0 text-xs font-medium text-on-surface-muted"
            aria-live="polite"
            data-testid="onboarding-progress"
          >
            {{ t('onboarding.progress', { done: doneCount, total }) }}
          </p>
        </div>
      </div>
      <UiButton
        variant="ghost"
        size="icon"
        :aria-label="complete ? t('onboarding.close') : t('onboarding.hide')"
        :title="complete ? t('onboarding.close') : t('onboarding.hide')"
        data-testid="onboarding-hide"
        @click="hide"
      >
        <X class="size-5" aria-hidden="true" />
      </UiButton>
    </header>

    <ol class="flex flex-col divide-y divide-border" data-testid="onboarding-steps">
      <li
        v-for="(step, index) in onboardingSteps"
        :key="step"
        class="flex gap-4 px-5 py-4 sm:px-6"
        :aria-current="step === nextStep ? 'step' : undefined"
        :data-testid="`onboarding-step-${step}`"
        :data-done="done[step]"
      >
        <span
          class="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold"
          :class="
            done[step]
              ? 'bg-primary text-on-primary'
              : step === nextStep
                ? 'border-2 border-primary text-primary'
                : 'border border-border-strong text-on-surface-muted'
          "
          aria-hidden="true"
        >
          <Check v-if="done[step]" class="size-4" />
          <template v-else>{{ index + 1 }}</template>
        </span>
        <div class="flex min-w-0 flex-1 flex-col gap-2 sm:flex-row sm:items-start sm:gap-4">
          <div class="flex min-w-0 flex-1 flex-col gap-0.5">
            <h3 class="text-sm font-semibold">
              <span :class="done[step] ? 'text-on-surface-muted line-through' : ''">{{
                t(`onboarding.steps.${step}.title`)
              }}</span>
              <span class="sr-only">
                ({{ done[step] ? t('onboarding.stepDone') : t('onboarding.stepOpen')
                }}{{ isOptional(step) ? `, ${t('onboarding.optional')}` : '' }})</span
              >
              <span
                v-if="isOptional(step)"
                class="ml-1.5 rounded-full bg-surface-hover px-2 py-0.5 text-xs font-medium text-on-surface-muted"
                aria-hidden="true"
                >{{ t('onboarding.optional') }}</span
              >
            </h3>
            <p v-if="!done[step]" class="text-sm text-on-surface-muted">
              {{ t(`onboarding.steps.${step}.text`) }}
            </p>
            <p
              v-if="step === 'group' && !done.group && !verified"
              class="text-sm text-on-warning-soft"
              data-testid="onboarding-verify-first"
            >
              {{ t('onboarding.steps.group.verifyFirst') }}
            </p>
            <p v-if="step === 'feed' && !done.feed" class="text-sm">
              <TextLink :to="{ name: 'help-calendar-apps' }">{{ t('feeds.help') }}</TextLink>
            </p>
            <p
              v-if="step === 'event' && !done.event && !done.calendar"
              class="text-sm text-on-surface-muted"
            >
              {{ t('onboarding.steps.event.needsCalendar') }}
            </p>
          </div>
          <div v-if="!done[step]" class="shrink-0">
            <UiButton
              v-if="step === 'calendar'"
              size="sm"
              :variant="step === nextStep ? 'primary' : 'secondary'"
              data-testid="onboarding-calendar"
              @click="calendarOpen = true"
            >
              {{ t('onboarding.steps.calendar.action') }}
            </UiButton>
            <template v-else-if="step === 'group'">
              <UiButton
                v-if="inviteGroup"
                as-child
                size="sm"
                :variant="step === nextStep ? 'primary' : 'secondary'"
              >
                <RouterLink
                  :to="{ name: 'group-invites', params: { groupId: inviteGroup.id } }"
                  data-testid="onboarding-invite"
                >
                  {{ t('onboarding.steps.group.invite') }}
                </RouterLink>
              </UiButton>
              <UiButton
                v-else
                size="sm"
                :variant="step === nextStep ? 'primary' : 'secondary'"
                data-testid="onboarding-group"
                @click="groupOpen = true"
              >
                {{ t('onboarding.steps.group.create') }}
              </UiButton>
            </template>
            <UiButton
              v-else-if="step === 'feed'"
              size="sm"
              :variant="step === nextStep ? 'primary' : 'secondary'"
              data-testid="onboarding-feed"
              @click="feedOpen = true"
            >
              {{ t('onboarding.steps.feed.action') }}
            </UiButton>
            <UiButton
              v-else-if="step === 'event' && done.calendar"
              as-child
              size="sm"
              :variant="step === nextStep ? 'primary' : 'secondary'"
            >
              <RouterLink
                :to="{ name: 'calendar', query: { new: '1' } }"
                data-testid="onboarding-event"
              >
                {{ t('onboarding.steps.event.action') }}
              </RouterLink>
            </UiButton>
          </div>
        </div>
      </li>
    </ol>

    <footer
      v-if="complete"
      class="flex items-center justify-end gap-2 rounded-b-2xl border-t border-border bg-surface-muted px-5 py-3 sm:px-6"
    >
      <PartyPopper class="mr-auto size-5 text-primary" aria-hidden="true" />
      <UiButton size="sm" data-testid="onboarding-close" @click="hide">
        {{ t('onboarding.close') }}
      </UiButton>
    </footer>

    <CreateCalendarDialog
      v-model:open="calendarOpen"
      :suggested-name="user?.displayName ?? ''"
      :open-settings="false"
    />
    <CreateGroupDialog v-model:open="groupOpen" next="group-invites" />
    <CreateFeedDialog v-model:open="feedOpen" />
  </section>
</template>
