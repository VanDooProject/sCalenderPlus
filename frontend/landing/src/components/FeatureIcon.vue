<script setup lang="ts">
import { computed, type Component } from 'vue'
import { Calendar, Eye, House, Info, Lock, RefreshCw, Shield, Users } from '@lucide/vue'

/** Icons the feature messages may name (`features.items[].icon`). */
const icons: Record<string, Component> = {
  calendar: Calendar,
  eye: Eye,
  home: House,
  info: Info,
  lock: Lock,
  refresh: RefreshCw,
  shield: Shield,
  users: Users,
}

const props = defineProps<{ name: string }>()
const icon = computed(() => {
  const found = icons[props.name]
  if (found === undefined) throw new Error(`Unknown feature icon "${props.name}"`)
  return found
})
</script>

<template>
  <component :is="icon" class="size-5" aria-hidden="true" />
</template>
