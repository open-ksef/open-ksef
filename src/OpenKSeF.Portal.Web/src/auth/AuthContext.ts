import { createContext } from 'react'
import type { User } from 'oidc-client-ts'

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
  login: () => Promise<void>
  loginWithCredentials: (username: string, password: string) => Promise<void>
  loginWithGoogle: (returnTo?: string) => Promise<void>
  register: (data: RegisterData) => Promise<void>
  logout: () => Promise<void>
  getAccessToken: () => Promise<string | null>
  handleOidcCallback: () => Promise<string>
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)
