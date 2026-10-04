<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { asRole } from '@/lib/groupRoles'

const props = defineProps<{ role: string }>()
const { t } = useI18n()

const styles = {
  owner: 'bg-primary-soft text-on-primary-soft',
  admin: 'bg-info-soft text-on-info-soft',
  member: 'bg-surface-hover text-on-surface',
  viewer: 'border border-border-strong text-on-surface-muted',
} as const
const role = computed(() => asRole(props.role))
</script>

<template>
  <span
    class="inline-flex h-6 items-center rounded-full px-2.5 text-xs font-medium whitespace-nowrap"
    :class="styles[role]"
    :data-role="role"
    data-testid="role-badge"
  >
    {{ t(`groups.roles.${role}`) }}
  </span>
</template>
