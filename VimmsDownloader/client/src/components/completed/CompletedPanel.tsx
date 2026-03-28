import { useData, useConvertPs3 } from '../../api/queries'
import { HistoryItem } from './HistoryItem'

export function CompletedPanel() {
  const { data } = useData()
  const convertAllMutation = useConvertPs3()

  const history = data?.history ?? []

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between px-3 sm:px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase">
          {history.length} completed
        </span>
        {/* Cross = blue = action */}
        <button
          onClick={() => convertAllMutation.mutate(undefined)}
          disabled={convertAllMutation.isPending}
          className="text-[10px] text-ps-cross/50 hover:text-[#7eb3e0] tracking-wide uppercase
            transition-colors disabled:opacity-40"
        >
          Convert All PS3
        </button>
      </div>

      <div className="flex-1 overflow-y-auto">
        {history.map(item => (
          <HistoryItem key={item.id} item={item} />
        ))}
        {history.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
            No completed downloads
          </div>
        )}
      </div>
    </div>
  )
}
