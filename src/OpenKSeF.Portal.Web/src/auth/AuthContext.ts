import { createContext } from 'react'
import type { User } from 'oidc-client-ts'

export interface AuthContextValue {
  user: User | null
  isLoading: boolean
  isAuthenticated: boolean
  sessionEnded: boolean
  login: (returnTo?: string) => Promise<void>
  logout: () => Promise<void>
  getAccessToken: () => Promise<string | null>
  handleOidcCallback: () => Promise<string>
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)
