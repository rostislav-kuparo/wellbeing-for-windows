import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './index.css'
import { ColorProvider, LiveProvider, ThemeProvider, ToastProvider } from './live'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <ToastProvider>
        <LiveProvider>
          <ColorProvider>
            <App />
          </ColorProvider>
        </LiveProvider>
      </ToastProvider>
    </ThemeProvider>
  </StrictMode>,
)
