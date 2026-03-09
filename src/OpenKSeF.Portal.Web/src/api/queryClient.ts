import { QueryCache, QueryClient } from '@tanstack/react-query'

export function createQueryClient(): QueryClient {
  return new QueryClient({
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
}

export const queryClient = createQueryClient()
