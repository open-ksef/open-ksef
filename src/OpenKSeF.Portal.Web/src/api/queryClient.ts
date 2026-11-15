import { QueryCache, QueryClient } from '@tanstack/react-query'

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
  queryCache: new QueryCache({
    onError: (error) => {
      console.error('React Query error', error)
    },
  }),
})
