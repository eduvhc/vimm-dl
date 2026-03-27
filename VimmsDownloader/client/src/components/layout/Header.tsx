import { useDownload } from '../../hooks/useDownloadState'

export function Header() {
  const { state } = useDownload()

  const dotColor =
    state.connectionState === 'connected' ? 'bg-ps-triangle' :
    state.connectionState === 'reconnecting' ? 'bg-amber' : 'bg-ps-circle'

  return (
    <header className="xmb-glass flex items-center justify-between px-6 py-3 border-b border-border/50">
      <div className="flex items-center gap-4">
        {/* PS3-style logo mark */}
        <div className="flex items-center gap-1">
          <div className="w-6 h-6 rounded-sm bg-gradient-to-br from-accent/40 to-accent/10 flex items-center justify-center">
            <span className="text-[10px] font-bold text-accent">V</span>
          </div>
          <h1 className="font-sans text-sm font-semibold tracking-[0.2em] uppercase text-text-2">
            Vimm<span className="text-accent/50 mx-px">/</span>DL
          </h1>
        </div>
      </div>
      <div className="flex items-center gap-2 text-[10px] text-text-4 tracking-widest uppercase">
        <div className={`w-1.5 h-1.5 rounded-full ${dotColor} shadow-[0_0_6px_currentColor]`} />
        <span>{state.connectionState}</span>
      </div>
    </header>
  )
}
