import type { ReactElement } from 'react'

import type { KsefSubmissionRequirement } from '@/api/schemas/invoice'

const requirementPresentation: Record<
  KsefSubmissionRequirement,
  { title: string; detail: string; tone: 'required' | 'optional' | 'forbidden' | 'na' }
> = {
  Required: {
    title: 'Wysyłka do KSeF jest wymagana',
    detail: 'Dokument powinien zostać przekazany do KSeF po zatwierdzeniu.',
    tone: 'required',
  },
  Optional: {
    title: 'Wysyłka do KSeF jest opcjonalna',
    detail: 'Dokument można wysłać do KSeF, ale nie jest to obowiązkowe.',
    tone: 'optional',
  },
  Forbidden: {
    title: 'Wysyłka do KSeF jest niedozwolona',
    detail: 'Ten dokument nie powinien być przekazywany do KSeF.',
    tone: 'forbidden',
  },
  NotApplicable: {
    title: 'KSeF nie ma zastosowania',
    detail: 'Dla tego dokumentu nie uruchamia się procesu wysyłki do KSeF.',
    tone: 'na',
  },
}

interface KsefRequirementBannerProps {
  requirement: KsefSubmissionRequirement
}

export function KsefRequirementBanner({ requirement }: KsefRequirementBannerProps): ReactElement {
  const presentation = requirementPresentation[requirement]

  return (
    <section className={`ksef-requirement-banner ksef-requirement-banner--${presentation.tone}`} role="note">
      <h2 className="ksef-requirement-banner__title">{presentation.title}</h2>
      <p className="ksef-requirement-banner__detail">{presentation.detail}</p>
    </section>
  )
}
