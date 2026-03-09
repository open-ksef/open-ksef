import { apiClient } from '../client'
import type { OnboardingStatusResponse } from '../types'

export interface SetupTokenResponse {
  setupToken: string
  expiresInSeconds: number
}

export function generateSetupToken(): Promise<SetupTokenResponse> {
  return apiClient.post<SetupTokenResponse>('/account/setup-token')
}

export function getOnboardingStatus(): Promise<OnboardingStatusResponse> {
  return apiClient.get<OnboardingStatusResponse>('/account/onboarding-status')
}
