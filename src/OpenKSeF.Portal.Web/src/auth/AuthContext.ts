import { createContext } from 'react'
import type { User } from 'oidc-client-ts'

export type LoginAction = 'signin' | 'signup'

export interface RegisterData {
  email: string
  password: string
  firstName?: string
  lastName?: string
}

export interface AuthContextValue {
  user: User | null
  isLoading: boolean
  isAuthenticated: boolean
  login: (action?: LoginAction) => Promise<void>
  loginWithCredentials: (username: string, password: string) => Promise<void>
  loginWithGoogle: () => Promise<void>
  register: (data: RegisterData) => Promise<void>
  logout: () => Promise<void>
  getAccessToken: () => Promise<string | null>
  handleOidcCallback: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)
