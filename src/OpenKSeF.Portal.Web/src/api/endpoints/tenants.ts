import { apiClient } from '../client'
import type {
  AcceptInviteResponse,
  CreateInviteRequest,
  CreateInviteResponse,
  CreateTenantRequest,
  InviteLinkResponse,
  TenantInviteResponse,
  TenantMemberResponse,
  TenantResponse,
  TenantRole,
  UpdateTenantRequest,
} from '../types'

const tenantPath = (id: string): string => `/tenants/${encodeURIComponent(id)}`

export function listTenants(): Promise<TenantResponse[]> {
  return apiClient.get<TenantResponse[]>('/tenants')
}

export function createTenant(request: CreateTenantRequest): Promise<TenantResponse> {
  return apiClient.post<TenantResponse>('/tenants', request)
}

export function updateTenant(id: string, request: UpdateTenantRequest): Promise<TenantResponse> {
  return apiClient.put<TenantResponse>(tenantPath(id), request)
}

export function deleteTenant(id: string): Promise<void> {
  return apiClient.delete<void>(tenantPath(id))
}

export function setMyNotifications(id: string, enabled: boolean): Promise<void> {
  return apiClient.put<void>(`${tenantPath(id)}/members/me/notifications`, { enabled })
}

export function listMembers(id: string): Promise<TenantMemberResponse[]> {
  return apiClient.get<TenantMemberResponse[]>(`${tenantPath(id)}/members`)
}

export function changeMemberRole(id: string, userId: string, role: TenantRole): Promise<void> {
  return apiClient.put<void>(`${tenantPath(id)}/members/${encodeURIComponent(userId)}`, { role })
}

export function removeMember(id: string, userId: string): Promise<void> {
  return apiClient.delete<void>(`${tenantPath(id)}/members/${encodeURIComponent(userId)}`)
}

export function listInvites(id: string): Promise<TenantInviteResponse[]> {
  return apiClient.get<TenantInviteResponse[]>(`${tenantPath(id)}/invites`)
}

export function createInvite(id: string, request: CreateInviteRequest): Promise<CreateInviteResponse> {
  return apiClient.post<CreateInviteResponse>(`${tenantPath(id)}/invites`, request)
}

export function resendInvite(id: string, inviteId: string): Promise<InviteLinkResponse> {
  return apiClient.post<InviteLinkResponse>(`${tenantPath(id)}/invites/${encodeURIComponent(inviteId)}/resend`)
}

export function revokeInvite(id: string, inviteId: string): Promise<void> {
  return apiClient.delete<void>(`${tenantPath(id)}/invites/${encodeURIComponent(inviteId)}`)
}

export function acceptInvite(token: string): Promise<AcceptInviteResponse> {
  return apiClient.post<AcceptInviteResponse>('/invites/accept', { token })
}
