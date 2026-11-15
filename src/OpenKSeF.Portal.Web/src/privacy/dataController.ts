export function dataControllerError(name: string, contact: string): string | null {
  const trimmedName = name.trim()
  const trimmedContact = contact.trim()
  if (!trimmedName) return 'Podaj nazwę administratora danych.'
  if (trimmedName.length > 200) return 'Nazwa administratora danych może mieć najwyżej 200 znaków.'
  if (!trimmedContact) return 'Podaj e-mail albo adres administratora danych.'
  if (trimmedContact.length > 300) return 'Kontakt do administratora danych może mieć najwyżej 300 znaków.'
  return null
}

export const isEmailContact = (contact: string): boolean => contact.includes('@') && !/\s/.test(contact)

export function hasAdminRole(accessToken: string | undefined): boolean {
  try {
    const payload = accessToken?.split('.')[1]
    if (!payload) return false
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const roles: unknown = JSON.parse(json)?.realm_access?.roles
    return Array.isArray(roles) && roles.includes('openksef-admin')
  } catch {
    return false
  }
}
