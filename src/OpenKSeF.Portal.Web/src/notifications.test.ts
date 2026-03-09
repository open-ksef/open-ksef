import { describe, expect, it, vi } from 'vitest'

import { notifyMutationError, notifyMutationSuccess } from './notifications'

const { success, error } = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
}))

vi.mock('react-hot-toast', () => ({
  default: {
    success,
    error,
  },
}))

describe('mutation notifications', () => {
  it('emits success toast with action/entity message', () => {
    notifyMutationSuccess('created', 'Tenant')

    expect(success).toHaveBeenCalledWith('Firma: utworzona pomyslnie')
  })

  it('emits error toast with fallback message', () => {
    notifyMutationError('delete', 'Device', undefined)

    expect(error).toHaveBeenCalledWith('Nie udalo sie usunac (Urzadzenie)')
  })

  it('emits error toast with error details when available', () => {
    notifyMutationError('update', 'Credential', new Error('Request failed'))

    expect(error).toHaveBeenCalledWith('Nie udalo sie zaktualizowac (Dane logowania): Request failed')
  })
})
