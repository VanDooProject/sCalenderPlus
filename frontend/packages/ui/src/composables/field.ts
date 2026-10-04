import { computed, inject, provide, type ComputedRef, type InjectionKey } from 'vue'

/** What a {@link UiField} tells the control inside it: ids for label/description wiring and validity. */
export interface FieldContext {
  /** Id of the control (`<label for>` points here). */
  id: ComputedRef<string>
  /** Space-separated ids of the hint and error texts, or undefined. */
  describedBy: ComputedRef<string | undefined>
  invalid: ComputedRef<boolean>
  required: ComputedRef<boolean>
}

const fieldKey: InjectionKey<FieldContext> = Symbol('UiField')

export function provideField(context: FieldContext): void {
  provide(fieldKey, context)
}

/**
 * The surrounding field's wiring, with props of the control taking precedence (a control may be used
 * without a field, e.g. a search box with an `aria-label`).
 */
export function useFieldControl(props: { id?: string; invalid?: boolean }): {
  id: ComputedRef<string | undefined>
  describedBy: ComputedRef<string | undefined>
  invalid: ComputedRef<boolean>
  required: ComputedRef<boolean>
} {
  const field = inject(fieldKey, null)
  return {
    id: computed(() => props.id ?? field?.id.value),
    describedBy: computed(() => field?.describedBy.value),
    invalid: computed(() => !!props.invalid || (field?.invalid.value ?? false)),
    required: computed(() => field?.required.value ?? false),
  }
}
