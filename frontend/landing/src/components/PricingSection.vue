<script setup lang="ts">
import { computed, ref } from 'vue'
import { Check, Minus } from '@lucide/vue'
import type { Plan } from '@scalenderplus/plans'
import { cell, formatNumber, formatPrice, visibleRows, type Cell } from '@/lib/pricing'
import { appLink, edition, plans, usePage } from '@/site'

/**
 * Pricing cards and the comparison table, generated from @scalenderplus/plans (plans.json, checked
 * against the backend's defaults) with the edition's overrides; nothing here hard-codes a number.
 */
const { locale, t } = usePage()
const period = ref<'monthly' | 'yearly'>('monthly')
const rows = computed(() => visibleRows(edition.importer))

function price(plan: Plan): string {
  return formatPrice(locale.value, plans.currency, plan.price[period.value])
}

function unit(plan: Plan): string {
  if (plan.price.perSeat)
    return t(period.value === 'monthly' ? 'pricing.perSeatMonth' : 'pricing.perSeatYear')
  return t(period.value === 'monthly' ? 'pricing.perMonth' : 'pricing.perYear')
}

function text(value: Cell): string {
  switch (value.kind) {
    case 'number':
      return formatNumber(locale.value, value.value)
    case 'unlimited':
      return t('pricing.unlimited')
    case 'included':
      return t('pricing.included')
    case 'notIncluded':
      return t('pricing.notIncluded')
    case 'days':
      return t('pricing.days', { count: value.value })
    case 'years':
      return t('pricing.years', { count: value.value })
    case 'message':
      return t(value.key)
  }
}

const periods = ['monthly', 'yearly'] as const
</script>

<template>
  <section id="pricing" class="border-t border-border" aria-labelledby="pricing-title">
    <div class="mx-auto max-w-6xl px-4 py-16 sm:px-6 md:py-24">
      <div class="max-w-2xl">
        <h2 id="pricing-title" class="text-3xl font-bold tracking-tight">
          {{ t('pricing.title') }}
        </h2>
        <p class="mt-4 text-lg text-on-surface-muted">{{ t('pricing.lead') }}</p>
      </div>

      <div
        class="mt-8 inline-flex rounded-lg border border-border bg-surface-muted p-1"
        role="group"
        :aria-label="t('pricing.billing')"
      >
        <button
          v-for="p in periods"
          :key="p"
          type="button"
          :aria-pressed="period === p"
          class="rounded-md px-4 py-1.5 text-sm font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          :class="
            period === p
              ? 'bg-surface-raised shadow-raised'
              : 'text-on-surface-muted hover:text-on-surface'
          "
          :data-testid="`period-${p}`"
          @click="period = p"
        >
          {{ t(`pricing.${p}`) }}
          <span v-if="p === 'yearly'" class="ml-1 text-xs font-semibold text-primary">
            {{ t('pricing.yearlyHint') }}
          </span>
        </button>
      </div>

      <ul class="mt-8 grid gap-6 md:grid-cols-3">
        <li
          v-for="plan in plans.plans"
          :key="plan.id"
          class="relative flex flex-col gap-4 rounded-xl border bg-surface-raised p-6 shadow-raised"
          :class="plan.id === 'pro' ? 'border-primary ring-1 ring-primary' : 'border-border'"
          :data-testid="`plan-${plan.id}`"
        >
          <div class="flex items-center gap-2">
            <h3 class="text-xl font-semibold">{{ t(`pricing.plans.${plan.id}.name`) }}</h3>
            <span
              v-if="plan.id === 'pro'"
              class="rounded-full bg-primary-soft px-2 py-0.5 text-xs font-semibold text-on-primary-soft"
            >
              {{ t('pricing.recommended') }}
            </span>
          </div>
          <p class="text-sm text-on-surface-muted">{{ t(`pricing.plans.${plan.id}.tagline`) }}</p>
          <p class="flex flex-wrap items-baseline gap-x-2">
            <span class="text-4xl font-bold tracking-tight" data-testid="plan-price">{{
              price(plan)
            }}</span>
            <span class="text-sm text-on-surface-muted">{{ unit(plan) }}</span>
          </p>
          <p v-if="plan.price.minSeats" class="-mt-2 text-sm text-on-surface-muted">
            {{ t('pricing.minSeats', { count: plan.price.minSeats }) }}
          </p>
          <a
            :href="appLink('/register')"
            class="mt-auto rounded-lg px-4 py-2.5 text-center font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
            :class="
              plan.id === 'pro'
                ? 'bg-primary text-on-primary hover:bg-primary-hover'
                : 'border border-border-strong hover:bg-surface-hover'
            "
          >
            {{ t(`pricing.plans.${plan.id}.cta`) }}
          </a>
        </li>
      </ul>

      <div class="mt-12 overflow-x-auto rounded-xl border border-border">
        <table class="w-full min-w-[36rem] text-left text-sm" data-testid="pricing-table">
          <caption class="sr-only">
            {{
              t('pricing.comparison')
            }}
          </caption>
          <thead class="bg-surface-muted">
            <tr>
              <th scope="col" class="px-4 py-3 font-semibold">{{ t('pricing.feature') }}</th>
              <th
                v-for="plan in plans.plans"
                :key="plan.id"
                scope="col"
                class="px-4 py-3 font-semibold"
              >
                {{ t(`pricing.plans.${plan.id}.name`) }}
              </th>
            </tr>
          </thead>
          <tbody class="divide-y divide-border">
            <tr>
              <th scope="row" class="px-4 py-3 font-medium">{{ t('pricing.feeds') }}</th>
              <td v-for="plan in plans.plans" :key="plan.id" class="px-4 py-3">
                {{ t('pricing.unlimited') }}
              </td>
            </tr>
            <tr v-for="row in rows" :key="row" :data-testid="`row-${row}`">
              <th scope="row" class="px-4 py-3 font-medium">{{ t(`pricing.limits.${row}`) }}</th>
              <td v-for="plan in plans.plans" :key="plan.id" class="px-4 py-3">
                <span class="inline-flex items-center gap-1.5">
                  <Check
                    v-if="cell(row, plan.limits[row]).kind === 'included'"
                    class="size-4 text-primary"
                    aria-hidden="true"
                  />
                  <Minus
                    v-else-if="cell(row, plan.limits[row]).kind === 'notIncluded'"
                    class="size-4 text-on-surface-muted"
                    aria-hidden="true"
                  />
                  <span
                    :class="{
                      'text-on-surface-muted': cell(row, plan.limits[row]).kind === 'notIncluded',
                    }"
                  >
                    {{ text(cell(row, plan.limits[row])) }}
                  </span>
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="mt-6 flex flex-col gap-2 text-sm text-on-surface-muted">
        <p>{{ t('pricing.trial') }}</p>
        <p>{{ t('pricing.seatsHint') }}</p>
        <p>{{ t('pricing.vat', { currency: plans.currency }) }}</p>
      </div>
    </div>
  </section>
</template>
