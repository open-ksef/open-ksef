import { apiClient } from '../client'
import type { OnboardingStatusResponse } from '../types'

export function getOnboardingStatus(): Promise<OnboardingStatusResponse> {
  return apiClient.get<OnboardingStatusResponse>('/account/onboarding-status')
}
