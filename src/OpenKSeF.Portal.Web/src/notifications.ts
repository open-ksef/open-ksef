import toast from 'react-hot-toast'

function getErrorMessage(error: unknown): string | null {
  if (error instanceof Error && error.message) {
    return error.message
  }

  if (typeof error === 'string' && error.trim()) {
    return error
  }

  return null
}

interface EntityInfo {
  name: string
  gender: 'f' | 'm' | 'n' | 'pl'
}

const entityPl: Record<string, EntityInfo> = {
  Tenant: { name: 'Firma', gender: 'f' },
  Credential: { name: 'Token KSeF', gender: 'm' },
  Certificate: { name: 'Certyfikat KSeF', gender: 'm' },
  Device: { name: 'Urządzenie', gender: 'n' },
}

const actionSuccessPl: Record<string, Record<'f' | 'm' | 'n' | 'pl', string>> = {
  created: { f: 'utworzona pomyślnie', m: 'utworzony pomyślnie', n: 'utworzone pomyślnie', pl: 'utworzone pomyślnie' },
  updated: { f: 'zaktualizowana pomyślnie', m: 'zaktualizowany pomyślnie', n: 'zaktualizowane pomyślnie', pl: 'zaktualizowane pomyślnie' },
  deleted: { f: 'usunięta pomyślnie', m: 'usunięty pomyślnie', n: 'usunięte pomyślnie', pl: 'usunięte pomyślnie' },
  saved: { f: 'zapisana pomyślnie', m: 'zapisany pomyślnie', n: 'zapisane pomyślnie', pl: 'zapisane pomyślnie' },
  registered: { f: 'zarejestrowana pomyślnie', m: 'zarejestrowany pomyślnie', n: 'zarejestrowane pomyślnie', pl: 'zarejestrowane pomyślnie' },
  unregistered: { f: 'wyrejestrowana pomyślnie', m: 'wyrejestrowany pomyślnie', n: 'wyrejestrowane pomyślnie', pl: 'wyrejestrowane pomyślnie' },
}

const actionErrorPl: Record<string, string> = {
  create: 'utworzyć',
  update: 'zaktualizować',
  delete: 'usunąć',
  save: 'zapisać',
  register: 'zarejestrować',
  unregister: 'wyrejestrować',
}

export function notifyMutationSuccess(action: string, entity: string): void {
  const info = entityPl[entity]
  const name = info?.name ?? entity
  const verbMap = actionSuccessPl[action]
  const verb = verbMap ? verbMap[info?.gender ?? 'f'] : `${action} pomyślnie`
  toast.success(`${name}: ${verb}`)
}

export function notifyMutationError(action: string, entity: string, error: unknown): void {
  const message = getErrorMessage(error)
  const name = entityPl[entity]?.name ?? entity
  const verb = actionErrorPl[action] ?? action
  toast.error(message ? `Nie udało się ${verb} (${name}): ${message}` : `Nie udało się ${verb} (${name})`)
}
