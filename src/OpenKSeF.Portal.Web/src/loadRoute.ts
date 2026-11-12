const RELOAD_FLAG = 'openksef:chunk-reload'

export async function loadRoute<T>(load: () => Promise<T>): Promise<T> {
  const module = await load()
  sessionStorage.removeItem(RELOAD_FLAG)
  return module
}

export function isChunkLoadError(error: unknown): boolean {
  return error instanceof Error && /dynamically imported module|module script failed|Unable to preload CSS/i.test(error.message)
}

export function shouldReloadForChunkError(error: unknown, path: string): boolean {
  return isChunkLoadError(error) && sessionStorage.getItem(RELOAD_FLAG) !== path
}

export function reloadForChunkError(path: string): void {
  sessionStorage.setItem(RELOAD_FLAG, path)
  location.reload()
}
