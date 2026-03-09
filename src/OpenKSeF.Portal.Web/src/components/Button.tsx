import type { ButtonHTMLAttributes, ReactElement, ReactNode } from 'react'

type ButtonVariant = 'primary' | 'secondary' | 'outline'
type ButtonSize = 'sm' | 'md' | 'lg'

interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  variant?: ButtonVariant
  size?: ButtonSize
  children: ReactNode
}

export function Button({ variant = 'primary', size = 'md', children, className, ...rest }: ButtonProps): ReactElement {
  const classes = ['ui-button', `ui-button--${variant}`, `ui-button--${size}`, className].filter(Boolean).join(' ')
  return (
    <button type="button" className={classes} {...rest}>
      {children}
    </button>
  )
}
