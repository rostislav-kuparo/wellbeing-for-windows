import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

/** Подключение к службе, запущенной из консоли (dev): %LOCALAPPDATA%\Wellbeing.Dev\agent.json */
function devConnection(): { baseUrl: string; token: string } | null {
  try {
    const path = join(process.env.LOCALAPPDATA ?? '', 'Wellbeing.Dev', 'agent.json')
    return JSON.parse(readFileSync(path, 'utf8'))
  } catch {
    return null
  }
}

export default defineConfig(() => {
  const connection = devConnection()
  const target = connection?.baseUrl ?? 'http://127.0.0.1:47614'

  return {
    plugins: [react(), tailwindcss()],
    base: '/',
    build: {
      outDir: '../src/Wellbeing.Service/wwwroot',
      emptyOutDir: true,
      chunkSizeWarningLimit: 1500,
    },
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        // В dev-режиме токен добавляет прокси — так же, как в окне агента его добавляет cookie.
        '/api': {
          target,
          changeOrigin: true,
          configure: (proxy) => {
            proxy.on('proxyReq', (request) => {
              request.setHeader('origin', target)
              if (connection) request.setHeader('authorization', `Bearer ${connection.token}`)
            })
          },
        },
      },
    },
  }
})
