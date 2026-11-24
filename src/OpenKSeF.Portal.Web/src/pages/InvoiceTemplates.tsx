import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link, useNavigate } from 'react-router-dom'

import { createDraftFromTemplate, deleteTemplate, listTemplates, renameTemplate } from '@/api/endpoints/issuedInvoices'
import type { InvoiceTemplateResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { AsyncStateView } from '@/components/AsyncStateView'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { SaveTemplateButton } from '@/components/invoicing/SaveTemplateButton'
import '@/components/invoicing/invoicing.css'
import { Table, type TableColumn } from '@/components/Table'
import { countryName } from '@/invoicing/foreign'
import { polishPlural } from '@/invoicing/format'

function buyerLine(template: InvoiceTemplateResponse): string {
  const { buyer } = template
  const id = buyer.kind === 'Company' ? (buyer.nip ? `NIP ${buyer.nip}` : null) : buyer.kind === 'Foreign' && buyer.countryCode ? countryName(buyer.countryCode) : null
  return [buyer.name ?? '—', id].filter(Boolean).join(' · ')
}

export function InvoiceTemplatesPage(): ReactElement {
  const { tenantId } = useCompany()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`
  const [toDelete, setToDelete] = useState<InvoiceTemplateResponse | null>(null)

  const templatesQuery = useQuery({
    queryKey: ['invoice-templates', tenantId],
    queryFn: () => listTemplates(tenantId),
    enabled: Boolean(tenantId),
  })
  const templates = templatesQuery.data ?? []

  const createDraft = useMutation({
    mutationFn: (id: string) => createDraftFromTemplate(tenantId, id),
    onSuccess: (draft) => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      navigate(`/issued/${encodeURIComponent(draft.id)}/edit?${tenantQuery}`)
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => deleteTemplate(tenantId, id),
    onSuccess: () => {
      setToDelete(null)
      void queryClient.invalidateQueries({ queryKey: ['invoice-templates', tenantId] })
    },
    onError: () => setToDelete(null),
  })

  const failure = createDraft.error ?? remove.error

  const columns: TableColumn<InvoiceTemplateResponse>[] = [
    {
      key: 'name',
      label: 'Szablon',
      render: (template) => (
        <>
          <b>{template.name}</b>
          <span className="received-sub">{buyerLine(template)}</span>
        </>
      ),
    },
    {
      key: 'lineCount',
      label: 'Zawartość',
      render: (template) =>
        `${template.lineCount} ${polishPlural(template.lineCount, ['pozycja', 'pozycje', 'pozycji'])} · ${template.currency} · termin ${template.paymentDays} ${polishPlural(template.paymentDays, ['dzień', 'dni', 'dni'])}`,
    },
    {
      key: 'id',
      label: 'Akcje',
      className: 'issued-actions',
      render: (template) => (
        <div className="status-panel__row">
          <button
            type="button"
            className="ui-button ui-button--primary ui-button--sm"
            aria-label={`Nowa z szablonu ${template.name}`}
            disabled={createDraft.isPending}
            onClick={() => createDraft.mutate(template.id)}
          >
            <Icon name="plus" /> Nowa z szablonu
          </button>
          <SaveTemplateButton
            label="Zmień nazwę"
            ariaLabel={`Zmień nazwę szablonu ${template.name}`}
            message="Zmieniasz tylko nazwę. Zawartość szablonu zostaje."
            defaultName={template.name}
            save={(name) => renameTemplate(tenantId, template.id, name)}
          />
          <button
            type="button"
            className="ui-button ui-button--danger-outline ui-button--sm"
            aria-label={`Usuń szablon ${template.name}`}
            onClick={() => setToDelete(template)}
          >
            <Icon name="trash-2" />
          </button>
        </div>
      ),
    },
  ]

  return (
    <div className="inv-stack">
      <Link to={`/issued?${tenantQuery}`} className="back-link">
        <Icon name="arrow-left" size={14} /> Wystawione
      </Link>
      <header className="page-header received-head">
        <h1>Szablony</h1>
      </header>
      <p className="inv-help">
        Szablon zapiszesz w szkicu albo w szczegółach faktury VAT. Nowa faktura z szablonu dostaje dzisiejsze daty, termin płatności z szablonu i numer przy wystawieniu.
      </p>
      {failure ? <ErrorBanner message={failure instanceof Error ? failure.message : 'Operacja nie powiodła się'} /> : null}
      <AsyncStateView
        isLoading={templatesQuery.isLoading}
        error={templatesQuery.error}
        isEmpty={templates.length === 0}
        emptyTitle="Nie masz jeszcze szablonów"
        emptyMessage="Otwórz szkic albo fakturę VAT i wybierz „Zapisz jako szablon”."
        onRetry={() => void templatesQuery.refetch()}
      >
        <section className="received-list" aria-label="Lista szablonów">
          <Table testId="template-table" columns={columns} data={templates} />
        </section>
      </AsyncStateView>
      <ConfirmDialog
        open={toDelete !== null}
        title="Usunąć szablon?"
        message={`Szablon „${toDelete?.name ?? ''}” zostanie usunięty. Faktury utworzone z niego zostają.`}
        confirmLabel="Usuń szablon"
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => toDelete && remove.mutate(toDelete.id)}
        onCancel={() => setToDelete(null)}
      />
    </div>
  )
}
