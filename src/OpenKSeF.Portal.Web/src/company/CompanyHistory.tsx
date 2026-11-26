import { useInfiniteQuery } from '@tanstack/react-query'
import type { ReactElement } from 'react'

import { listHistory } from '@/api/endpoints/tenants'
import type { TenantHistoryEventResponse } from '@/api/types'
import { historyDetails, historyLabel } from '@/company/history'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { Table, type TableColumn } from '@/components/Table'
import { formatDateTime } from '@/invoicing/format'

const COLUMNS: TableColumn<TenantHistoryEventResponse>[] = [
  { key: 'occurredAt', label: 'Data', className: 'mono', render: (event) => formatDateTime(event.occurredAt) },
  { key: 'actor', label: 'Kto', render: (event) => (event.actorDeleted ? 'Usunięte konto' : event.actor) },
  { key: 'action', label: 'Zdarzenie', render: (event) => historyLabel(event.action) },
  { key: 'target', label: 'Szczegóły', render: historyDetails },
]

export function CompanyHistory({ tenantId }: { tenantId: string }): ReactElement {
  const history = useInfiniteQuery({
    queryKey: ['tenant-history', tenantId],
    queryFn: ({ pageParam }) => listHistory(tenantId, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.hasMore ? last.items.at(-1)?.id : undefined),
  })
  const items = history.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <section className="ui-card company-grid__full" aria-labelledby="company-history-title" data-testid="company-history">
      <h2 id="company-history-title">Historia zmian</h2>
      <AsyncStateView
        isLoading={history.isPending}
        error={history.error}
        isEmpty={items.length === 0}
        emptyTitle="Brak zdarzeń"
        emptyMessage="Tu pojawią się zmiany w firmie: dostęp osób, dane KSeF, opłacenia i eksporty."
        onRetry={() => void history.refetch()}
      >
        <Table columns={COLUMNS} data={items} testId="company-history-table" />
        {history.hasNextPage ? (
          <Button variant="outline" data-testid="company-history-more" disabled={history.isFetchingNextPage} onClick={() => void history.fetchNextPage()}>
            {history.isFetchingNextPage ? 'Ładowanie…' : 'Pokaż starsze'}
          </Button>
        ) : null}
      </AsyncStateView>
    </section>
  )
}
