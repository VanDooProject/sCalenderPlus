<script setup lang="ts">
import { usePage } from '@/site'

/** FAQ as native disclosure widgets: work before hydration and without JavaScript. */
const { t, tm, fmt } = usePage()
const items = () => tm<{ q: string; a: string }[]>('faq.items')
</script>

<template>
  <section id="faq" class="border-t border-border bg-surface-muted" aria-labelledby="faq-title">
    <div class="mx-auto max-w-3xl px-4 py-16 sm:px-6 md:py-24">
      <h2 id="faq-title" class="text-3xl font-bold tracking-tight">{{ t('faq.title') }}</h2>
      <div class="mt-8 flex flex-col gap-3">
        <details
          v-for="item in items()"
          :key="item.q"
          class="group rounded-xl border border-border bg-surface-raised px-5 py-4"
        >
          <summary
            class="cursor-pointer list-none rounded font-semibold focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-focus [&::-webkit-details-marker]:hidden"
          >
            <span class="flex items-center justify-between gap-4">
              {{ fmt(item.q) }}
              <span
                class="text-xl leading-none text-on-surface-muted transition-transform group-open:rotate-45"
                aria-hidden="true"
                >+</span
              >
            </span>
          </summary>
          <p class="mt-3 leading-relaxed text-on-surface-muted">{{ fmt(item.a) }}</p>
        </details>
      </div>
    </div>
  </section>
</template>
