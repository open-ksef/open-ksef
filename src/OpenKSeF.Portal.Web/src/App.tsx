import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { Toaster } from 'react-hot-toast'
import { RouterProvider } from 'react-router-dom'

import { AuthProvider } from '@/auth/AuthProvider'
import { createAppRouter } from '@/router'
import '@/components/ui.css'

const router = createAppRouter()

function App() {
  return (
    <AuthProvider>
      <RouterProvider router={router} />
      <Toaster
        position="top-right"
        toastOptions={{
          duration: 4000,
          style: {
            border: '1px solid #d1d5db',
            borderRadius: '8px',
            fontSize: '14px',
          },
        }}
      />
      {import.meta.env.DEV ? <ReactQueryDevtools initialIsOpen={false} /> : null}
    </AuthProvider>
  )
}

export default App
