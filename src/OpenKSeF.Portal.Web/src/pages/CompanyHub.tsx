import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { Icon, type IconName } from '@/components/Icon'

const links: { to: string; label: string; icon: IconName; testId: string }[] = [
  { to: '/seller-profile', label: 'Dane do faktur', icon: 'file-text', testId: 'company-hub-link-data' },
  { to: '/credentials', label: 'Dane logowania', icon: 'key-round', testId: 'company-hub-link-credentials' },
  { to: '/tenants', label: 'Firmy', icon: 'building-2', testId: 'company-hub-link-tenants' },
]

export function CompanyHubPage(): ReactElement {
  return (
    <section>
      <header className="page-header">
        <h1>Firma</h1>
      </header>
      <ul className="company-hub">
        {links.map((link) => (
          <li key={link.to}>
            <Link to={link.to} className="company-hub__link" data-testid={link.testId}>
              <Icon name={link.icon} size={18} />
              <span>{link.label}</span>
              <Icon name="chevron-right" />
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
