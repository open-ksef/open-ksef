import { useQuery } from '@tanstack/react-query'

import { credentialsOf, getCredentials, getLinkedAccounts } from '@/auth/account'

export function useSignInMethods() {
  const credentials = useQuery({ queryKey: ['account', 'credentials'], queryFn: getCredentials })
  const linked = useQuery({ queryKey: ['account', 'linked-accounts'], queryFn: getLinkedAccounts })
  const password = credentialsOf(credentials.data, 'password').length > 0
  const google = (linked.data ?? []).some((account) => account.providerAlias === 'google' && account.connected)
  return { credentials, linked, password, google }
}
