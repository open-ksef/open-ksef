const RELOAD_FLAG = 'openksef:chunk-reload'

export async function loadRoute<T>(load: () => Promise<T>): Promise<T> {
  try {
    const module = await load()
    sessionStorage.removeItem(RELOAD_FLAG)
    return module
  } catch (error) {
    if (sessionStorage.getItem(RELOAD_FLAG)) throw error
    sessionStorage.setItem(RELOAD_FLAG, '1')
    location.reload()
    return new Promise<T>(() => {})
  }
}
