// Мост к агенту через WebView2 (window.chrome.webview). В обычном браузере (dev) его нет.

interface WebViewBridge {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void
  removeEventListener(type: 'message', listener: (event: MessageEvent) => void): void
}

const webview = (window as unknown as { chrome?: { webview?: WebViewBridge } }).chrome?.webview

export const hasNativeBridge = webview !== undefined

/** Нативный диалог выбора .exe. Возвращает полный путь или null, если пользователь передумал. */
export function pickExe(): Promise<string | null> {
  if (!webview) return Promise.resolve(null)
  const requestId = Math.random().toString(36).slice(2)
  return new Promise((resolve) => {
    const listener = (event: MessageEvent) => {
      const data = event.data as { type?: string; requestId?: string; path?: string | null }
      if (data?.type !== 'pickExeResult' || data.requestId !== requestId) return
      webview.removeEventListener('message', listener)
      resolve(data.path ?? null)
    }
    webview.addEventListener('message', listener)
    webview.postMessage({ type: 'pickExe', requestId })
  })
}

/** Сообщает окну агента о теме — чтобы заголовок окна был в цвет. */
export function notifyTheme(theme: 'dark' | 'light') {
  webview?.postMessage({ type: 'theme', value: theme })
}
