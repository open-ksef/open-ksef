import type { ReactElement } from 'react'

interface Props {
  idPrefix: string
  name: string
  contact: string
  onNameChange: (value: string) => void
  onContactChange: (value: string) => void
  contactHint?: string
}

export function DataControllerFields({ idPrefix, name, contact, onNameChange, onContactChange, contactHint }: Props): ReactElement {
  return (
    <>
      <div className="ui-form-group">
        <label htmlFor={`${idPrefix}-controller-name`}>Nazwa administratora danych</label>
        <input id={`${idPrefix}-controller-name`} data-testid={`${idPrefix}-controller-name`} type="text" maxLength={200}
          placeholder="Moja Firma Sp. z o.o. albo Jan Kowalski"
          value={name} onInput={e => onNameChange((e.target as HTMLInputElement).value)} />
      </div>
      <div className="ui-form-group">
        <label htmlFor={`${idPrefix}-controller-contact`}>Kontakt do administratora danych</label>
        <input id={`${idPrefix}-controller-contact`} data-testid={`${idPrefix}-controller-contact`} type="text" maxLength={300}
          placeholder="rodo@firma.pl albo ul. Przykładowa 1, 00-001 Warszawa"
          aria-describedby={contactHint ? `${idPrefix}-controller-contact-hint` : undefined}
          value={contact} onInput={e => onContactChange((e.target as HTMLInputElement).value)} />
        {contactHint && <span id={`${idPrefix}-controller-contact-hint`} className="ui-form-hint">{contactHint}</span>}
      </div>
    </>
  )
}
