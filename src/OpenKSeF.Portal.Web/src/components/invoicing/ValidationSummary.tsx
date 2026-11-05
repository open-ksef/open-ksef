import type { ReactElement } from 'react'

import type { ValidationIssue } from '@/api/types'

interface ValidationSummaryProps {
  errors: ValidationIssue[]
  warnings: ValidationIssue[]
}

export function ValidationSummary({ errors, warnings }: ValidationSummaryProps): ReactElement | null {
  if (errors.length === 0 && warnings.length === 0) {
    return null
  }

  return (
    <>
      {errors.length > 0 ? (
        <div className="inv-alert inv-alert--error" role="alert">
          <strong>Popraw błędy przed wystawieniem faktury:</strong>
          <ul>
            {errors.map((issue) => (
              <li key={`${issue.code}-${issue.field ?? ''}`} data-code={issue.code}>
                {issue.message}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {warnings.length > 0 ? (
        <div className="inv-alert inv-alert--warning" role="status">
          <ul>
            {warnings.map((issue) => (
              <li key={`${issue.code}-${issue.field ?? ''}`} data-code={issue.code}>
                {issue.message}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </>
  )
}
