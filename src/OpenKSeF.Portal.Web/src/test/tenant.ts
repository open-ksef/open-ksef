import type { TenantResponse } from '@/api/types'

export const ownerAccess: Pick<TenantResponse, 'role' | 'permissions' | 'notificationsEnabled'> = {
  role: 'Owner',
  permissions: ['credential.manage', 'invoices.markPaid', 'invoices.read', 'issuing.draft', 'issuing.submit', 'members.manage', 'notifications.self', 'sync.fullResync', 'sync.run', 'tenant.delete', 'tenant.edit'],
  notificationsEnabled: true,
}

export const employeeAccess: Pick<TenantResponse, 'role' | 'permissions' | 'notificationsEnabled'> = {
  role: 'Employee',
  permissions: ['invoices.markPaid', 'invoices.read', 'issuing.draft', 'notifications.self', 'sync.run'],
  notificationsEnabled: true,
}
