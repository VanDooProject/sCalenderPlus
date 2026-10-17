<script setup lang="ts">
import AccessDemo from '@/components/AccessDemo.vue'
import FaqSection from '@/components/FaqSection.vue'
import FeatureIcon from '@/components/FeatureIcon.vue'
import PricingSection from '@/components/PricingSection.vue'
import { appLink, usePage } from '@/site'

const { t, tm } = usePage()
const features = () => tm<{ icon: string; title: string; text: string }[]>('features.items')
</script>

<template>
  <div>
    <section class="relative overflow-hidden" aria-labelledby="hero-title">
      <div
        class="mx-auto grid max-w-6xl items-center gap-12 px-4 py-16 sm:px-6 md:py-24 lg:grid-cols-[1.1fr_1fr]"
      >
        <div class="flex flex-col gap-6">
          <p class="text-sm font-semibold tracking-wide text-primary uppercase">
            {{ t('hero.eyebrow') }}
          </p>
          <h1
            id="hero-title"
            class="text-4xl leading-tight font-bold tracking-tight text-balance sm:text-5xl"
          >
            {{ t('hero.title') }}
          </h1>
          <p class="max-w-xl text-lg leading-relaxed text-on-surface-muted">{{ t('hero.lead') }}</p>
          <div class="flex flex-wrap gap-3">
            <a
              :href="appLink('/register')"
              class="rounded-lg bg-primary px-5 py-3 font-semibold text-on-primary shadow-raised hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
              data-testid="hero-cta"
            >
              {{ t('hero.primary') }}
            </a>
            <a
              href="#features"
              class="rounded-lg border border-border-strong px-5 py-3 font-semibold hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
            >
              {{ t('hero.secondary') }}
            </a>
          </div>
          <p class="text-sm text-on-surface-muted">{{ t('hero.note') }}</p>
        </div>
        <AccessDemo />
      </div>
    </section>

    <section
      id="features"
      class="border-t border-border bg-surface-muted"
      aria-labelledby="features-title"
    >
      <div class="mx-auto max-w-6xl px-4 py-16 sm:px-6 md:py-24">
        <div class="max-w-2xl">
          <h2 id="features-title" class="text-3xl font-bold tracking-tight text-balance">
            {{ t('features.title') }}
          </h2>
          <p class="mt-4 text-lg text-on-surface-muted">{{ t('features.lead') }}</p>
        </div>
        <ul class="mt-12 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          <li
            v-for="feature in features()"
            :key="feature.title"
            class="flex flex-col gap-3 rounded-xl border border-border bg-surface-raised p-6 shadow-raised"
          >
            <span
              class="inline-flex size-10 items-center justify-center rounded-lg bg-primary-soft text-on-primary-soft"
            >
              <FeatureIcon :name="feature.icon" />
            </span>
            <h3 class="text-lg font-semibold">{{ feature.title }}</h3>
            <p class="leading-relaxed text-on-surface-muted">{{ feature.text }}</p>
          </li>
        </ul>
      </div>
    </section>

    <PricingSection />
    <FaqSection />

    <section class="border-t border-border" aria-labelledby="cta-title">
      <div
        class="mx-auto flex max-w-3xl flex-col items-center gap-5 px-4 py-16 text-center sm:px-6"
      >
        <h2 id="cta-title" class="text-3xl font-bold tracking-tight text-balance">
          {{ t('cta.title') }}
        </h2>
        <p class="text-lg text-on-surface-muted">{{ t('cta.text') }}</p>
        <a
          :href="appLink('/register')"
          class="rounded-lg bg-primary px-5 py-3 font-semibold text-on-primary shadow-raised hover:bg-primary-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
          {{ t('cta.button') }}
        </a>
      </div>
    </section>
  </div>
</template>
