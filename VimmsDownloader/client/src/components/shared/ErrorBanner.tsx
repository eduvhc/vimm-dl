import { useDownload } from '../../hooks/useDownloadState'

export function ErrorBanner() {
  const { state, dispatch } = useDownload()

  if (state.errors.length === 0) return null

  return (
    <div className="mx-6 mt-2 p-2 bg-ps-circle/8 border border-ps-circle/15 rounded max-h-20 overflow-y-auto">
      <div className="flex items-center justify-between mb-1">
        <span className="text-[10px] font-medium text-[#e06070] tracking-wide uppercase">Errors</span>
        <button
          onClick={() => dispatch({ type: 'CLEAR_ERRORS' })}
          className="text-[10px] text-ps-circle/40 hover:text-ps-circle transition-colors"
        >
          Clear
        </button>
      </div>
      {state.errors.map((err, i) => (
        <div key={i} className="text-[10px] text-[#e06070]/70 font-mono truncate">{err}</div>
      ))}
    </div>
  )
}
