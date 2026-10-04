import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import {
  UiAlert,
  UiButton,
  UiCheckbox,
  UiDialog,
  UiField,
  UiInput,
  UiMenu,
  UiMenuItem,
  UiSelect,
  UiSpinner,
  UiToaster,
  useToast,
} from '../index'

afterEach(() => {
  document.body.innerHTML = ''
  useToast().clear()
})

describe('UiButton', () => {
  it('is a button of the given type and emits clicks', async () => {
    const wrapper = mount(UiButton, { props: { type: 'submit' }, slots: { default: 'Save' } })
    const button = wrapper.get('button')
    expect(button.attributes('type')).toBe('submit')
    expect(button.text()).toBe('Save')
    await button.trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('is disabled and busy while loading', () => {
    const wrapper = mount(UiButton, { props: { loading: true }, slots: { default: 'Save' } })
    const button = wrapper.get('button')
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.attributes('aria-busy')).toBe('true')
    expect(button.find('svg').exists()).toBe(true)
  })

  it('renders its child with the button styles (asChild)', () => {
    const wrapper = mount(UiButton, {
      props: { asChild: true },
      slots: { default: () => h('a', { href: '/next' }, 'Go') },
    })
    expect(wrapper.find('button').exists()).toBe(false)
    const link = wrapper.get('a')
    expect(link.attributes('href')).toBe('/next')
    expect(link.classes()).toContain('bg-primary')
  })
})

describe('UiField with UiInput', () => {
  function mountField(props: Record<string, unknown>) {
    return mount(
      defineComponent({
        setup() {
          const value = ref('')
          return () =>
            h(UiField, { label: 'Email', ...props }, () =>
              h(UiInput, {
                modelValue: value.value,
                'onUpdate:modelValue': (v: string) => (value.value = v),
                type: 'email',
              }),
            )
        },
      }),
    )
  }

  it('labels the input and describes it with the hint', () => {
    const wrapper = mountField({ hint: 'We never share it.', required: true })
    const input = wrapper.get('input')
    const label = wrapper.get('label')
    expect(label.attributes('for')).toBe(input.attributes('id'))
    expect(input.attributes('type')).toBe('email')
    expect(input.attributes('required')).toBeDefined()
    const hintId = input.attributes('aria-describedby')!
    expect(wrapper.get(`[id="${hintId}"]`).text()).toBe('We never share it.')
    expect(input.attributes('aria-invalid')).toBeUndefined()
  })

  it('marks the input invalid and links the error messages', () => {
    const wrapper = mountField({ errors: ['Enter a valid email address.', 'Too long.'] })
    const input = wrapper.get('input')
    expect(input.attributes('aria-invalid')).toBe('true')
    const errorId = input.attributes('aria-describedby')!
    const error = wrapper.get(`[id="${errorId}"]`)
    expect(error.text()).toContain('Enter a valid email address.')
    expect(error.text()).toContain('Too long.')
  })

  it('updates the model', async () => {
    const wrapper = mount(UiInput, { props: { modelValue: '' } })
    await wrapper.get('input').setValue('mia@example.test')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['mia@example.test'])
  })
})

describe('UiCheckbox', () => {
  it('toggles its model and is labelled', async () => {
    const wrapper = mount(UiCheckbox, {
      props: { label: 'Keep me signed in', modelValue: false },
      attachTo: document.body,
    })
    const box = wrapper.get('[role="checkbox"]')
    expect(box.attributes('aria-checked')).toBe('false')
    expect(wrapper.get('label').attributes('for')).toBe(box.attributes('id'))
    await box.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([true])
  })
})

describe('UiSelect', () => {
  it('shows the selected option and takes the field label', async () => {
    const wrapper = mount(
      defineComponent({
        setup: () => () =>
          h(UiField, { label: 'Language', errors: 'Pick one.' }, () =>
            h(UiSelect, {
              modelValue: 'de',
              options: [
                { value: 'en', label: 'English' },
                { value: 'de', label: 'Deutsch' },
              ],
            }),
          ),
      }),
    )
    await flushPromises()
    const trigger = wrapper.get('[role="combobox"]')
    expect(trigger.text()).toContain('Deutsch')
    expect(wrapper.get('label').attributes('for')).toBe(trigger.attributes('id'))
    expect(trigger.attributes('aria-invalid')).toBe('true')
  })
})

describe('UiDialog', () => {
  it('opens as a labelled modal dialog and closes with its close button', async () => {
    const open = ref(true)
    mount(
      defineComponent({
        setup: () => () =>
          h(
            UiDialog,
            {
              open: open.value,
              'onUpdate:open': (v: boolean) => (open.value = v),
              title: 'Turn off 2FA?',
              description: 'Confirm with your password.',
              closeLabel: 'Close',
            },
            { default: () => h('p', 'Body') },
          ),
      }),
      { attachTo: document.body },
    )
    await flushPromises()
    const dialog = document.querySelector('[role="dialog"]')!
    expect(dialog).not.toBeNull()
    const labelledBy = dialog.getAttribute('aria-labelledby')!
    expect(document.getElementById(labelledBy)?.textContent).toBe('Turn off 2FA?')
    expect(dialog.textContent).toContain('Body')
    ;(dialog.querySelector('button[aria-label="Close"]') as HTMLButtonElement).click()
    await flushPromises()
    expect(open.value).toBe(false)
  })
})

describe('UiMenu', () => {
  it('opens from its trigger and selects an item', async () => {
    const selected: string[] = []
    const wrapper = mount(
      defineComponent({
        setup: () => () =>
          h(
            UiMenu,
            {},
            {
              trigger: () => h('button', { type: 'button', 'data-testid': 'trigger' }, 'Account'),
              default: () =>
                h(UiMenuItem, { onSelect: () => selected.push('logout') }, () => 'Sign out'),
            },
          ),
      }),
      { attachTo: document.body },
    )
    const trigger = wrapper.get('[data-testid="trigger"]')
    expect(trigger.attributes('aria-haspopup')).toBe('menu')
    await trigger.trigger('keydown', { key: 'Enter' })
    await flushPromises()
    const item = document.querySelector('[role="menuitem"]') as HTMLElement
    expect(item.textContent).toContain('Sign out')
    item.click()
    await flushPromises()
    expect(selected).toEqual(['logout'])
  })
})

describe('UiToaster', () => {
  it('shows queued toasts and closes them', async () => {
    mount(UiToaster, { attachTo: document.body })
    const toast = useToast()
    toast.success('Profile saved.', 'All good')
    toast.error('Something went wrong.')
    await flushPromises()
    const toasts = document.querySelectorAll('[data-testid="toast"]')
    expect(toasts).toHaveLength(2)
    expect(toasts[0]!.textContent).toContain('Profile saved.')
    expect(toasts[0]!.getAttribute('data-variant')).toBe('success')
    expect(toasts[1]!.getAttribute('data-variant')).toBe('error')

    const first = toast.toasts.value[0]!
    toast.dismiss(first.id)
    await nextTick()
    expect(toast.toasts.value[0]!.open).toBe(false)
  })

  it('keeps at most four toasts open', () => {
    const toast = useToast()
    for (let i = 0; i < 6; i++) toast.info(`Toast ${i}`)
    expect(toast.toasts.value.filter((t) => t.open)).toHaveLength(4)
  })
})

describe('UiSpinner and UiAlert', () => {
  it('announces a labelled spinner and hides a decorative one', () => {
    expect(
      mount(UiSpinner, { props: { label: 'Loading' } })
        .get('[role="status"]')
        .text(),
    ).toBe('Loading')
    expect(mount(UiSpinner).attributes('aria-hidden')).toBe('true')
  })

  it('renders an alert with title and role', () => {
    const wrapper = mount(UiAlert, {
      props: { variant: 'danger', title: 'Failed', role: 'alert' },
      slots: { default: 'Try again.' },
    })
    expect(wrapper.attributes('role')).toBe('alert')
    expect(wrapper.text()).toContain('Failed')
    expect(wrapper.text()).toContain('Try again.')
  })
})
