import { useEffect, type ReactElement } from 'react'

import { Button } from '@/components/Button'

interface ConfirmDialogProps {
  open: boolean
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'danger' | 'default'
  isPending?: boolean
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Potwierdź',
  cancelLabel = 'Anuluj',
  variant = 'default',
  isPending = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps): ReactElement | null {
  useEffect(() => {
    if (!open) return

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onCancel()
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [open, onCancel])

  if (!open) return null

  return (
    <div
      className="ui-modal-backdrop"
      onClick={(e) => {
        if (e.target === e.currentTarget) onCancel()
      }}
    >
      <div
        className="ui-modal"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        aria-describedby="confirm-dialog-message"
        style={{ maxWidth: 420 }}
      >
        <div className="ui-modal-header">
          <h2 className="ui-modal-title" id="confirm-dialog-title">
            {title}
          </h2>
          <button
            type="button"
            className="ui-modal-close"
            aria-label="Zamknij"
            onClick={onCancel}
          >
            ✕
          </button>
        </div>

        <div className="ui-modal-body">
          <p id="confirm-dialog-message" style={{ margin: 0, fontSize: 14, color: 'var(--ui-text-muted)' }}>
            {message}
          </p>
        </div>

        <div className="ui-modal-footer">
          <Button variant="outline" onClick={onCancel}>
            {cancelLabel}
          </Button>
          <button
            type="button"
            className={`ui-button ui-button--md ${variant === 'danger' ? 'ui-button--danger' : 'ui-button--primary'}`}
            onClick={onConfirm}
            disabled={isPending}
          >
            {isPending ? 'Usuwanie…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
