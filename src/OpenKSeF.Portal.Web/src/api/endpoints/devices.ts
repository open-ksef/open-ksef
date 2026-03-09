import { apiClient } from '../client'
import type { DeviceTokenResponse } from '../types'

export interface RegisterDeviceRequest {
  token: string
  platform: number
  tenantId?: string | null
}

interface MessageResponse {
  message: string
}

export function listDevices(): Promise<DeviceTokenResponse[]> {
  return apiClient.get<DeviceTokenResponse[]>('/devices')
}

export function registerDevice(request: RegisterDeviceRequest): Promise<MessageResponse> {
  return apiClient.post<MessageResponse>('/devices/register', request)
}

export function unregisterDevice(token: string): Promise<void> {
  return apiClient.delete<void>(`/devices/${encodeURIComponent(token)}`)
}

export interface TestNotificationResponse {
  success: boolean
  error?: string
}

export function sendTestNotification(token: string): Promise<TestNotificationResponse> {
  return apiClient.post<TestNotificationResponse>(`/devices/${encodeURIComponent(token)}/test-notification`)
}
