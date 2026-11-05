import type { ReactElement } from 'react'

export type IconName =
  | 'inbox'
  | 'send'
  | 'building-2'
  | 'smartphone'
  | 'settings'
  | 'search'
  | 'cloud-download'
  | 'chevron-down'
  | 'chevron-right'
  | 'check'
  | 'x'
  | 'circle-check'
  | 'circle-x'
  | 'circle-alert'
  | 'triangle-alert'
  | 'clock'
  | 'pencil'
  | 'loader'
  | 'copy'
  | 'file-text'
  | 'plus'
  | 'trash-2'
  | 'arrow-left'
  | 'sun'
  | 'moon'
  | 'user'
  | 'log-out'
  | 'house'
  | 'key-round'
  | 'qr-code'
  | 'shield-check'
  | 'file-down'
  | 'circle-dashed'
  | 'landmark'

export function Icon({ name, size = 16 }: { name: IconName; size?: number }): ReactElement {
  return (
    <svg
      className="ui-icon"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <use href={`/icons.svg#i-${name}`} />
    </svg>
  )
}
