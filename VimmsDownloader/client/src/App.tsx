import { useState, useReducer, useEffect } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Header } from './components/layout/Header'
import { Toolbar } from './components/layout/Toolbar'
import { ControlBar } from './components/layout/ControlBar'
import { TabBar, type Tab } from './components/layout/TabBar'
import { StatusBar } from './components/layout/StatusBar'
import { ErrorBanner } from './components/shared/ErrorBanner'
import { UpdateBanner } from './components/shared/UpdateBanner'
import { ActivePanel } from './components/active/ActivePanel'
import { CompletedPanel } from './components/completed/CompletedPanel'
import { SyncPanel } from './components/sync/SyncPanel'
import { SettingsPanel } from './components/layout/SettingsPanel'
import { DownloadContext, downloadReducer, initialState } from './hooks/useDownloadState'
import { useSignalR } from './hooks/useSignalR'
import { useData, useSettings } from './api/queries'
import { parseProgress } from './lib/format'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

function AppContent() {
  const [activeTab, setActiveTab] = useState<Tab>('active')
  const [state, dispatch] = useReducer(downloadReducer, initialState)
  const connection = useSignalR(dispatch)
  const { data } = useData()

  // Restore running state from /api/data response
  useEffect(() => {
    if (!data) return
    dispatch({
      type: 'SET_RUNNING',
      running: data.isRunning,
      paused: data.isPaused,
    })
    if (data.currentUrl) {
      dispatch({ type: 'STATUS', url: `Processing: ${data.currentUrl}` })
    }
    if (data.progress) {
      const info = parseProgress(data.progress)
      if (info) dispatch({ type: 'PROGRESS', info })
    }
  }, [data?.isRunning, data?.isPaused, data?.currentUrl]) // eslint-disable-line react-hooks/exhaustive-deps


  // Update document title with progress
  useEffect(() => {
    if (state.activeDlInfo && state.running) {
      document.title = `${state.activeDlInfo.pct} — ${state.activeDlInfo.filename}`
    } else {
      document.title = 'VIMM//DL'
    }
  }, [state.activeDlInfo, state.running])

  const queued = data?.queued ?? []
  const history = data?.history ?? []
  const convertingCount = Object.values(state.convStatuses).filter(
    s => ['queued', 'extracting', 'extracted', 'converting'].includes(s.phase)
  ).length

  const counts = {
    active: queued.length + convertingCount,
    completed: history.length,
    sync: 0,
  }

  return (
    <DownloadContext.Provider value={{ state, dispatch, connection }}>
      <div className="flex flex-col h-screen bg-bg">
        <Header />
        <UpdateBanner />
        <Toolbar />
        <ControlBar />
        <ErrorBanner />
        <TabBar activeTab={activeTab} onTabChange={setActiveTab} counts={counts} />
        <main className="flex-1 overflow-hidden">
          {activeTab === 'active' && <ActivePanel />}
          {activeTab === 'completed' && <CompletedPanel />}
          {activeTab === 'sync' && <SyncPanel />}
          {activeTab === 'settings' && <SettingsPanel />}
        </main>
        <StatusBar />
      </div>
    </DownloadContext.Provider>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppContent />
    </QueryClientProvider>
  )
}
