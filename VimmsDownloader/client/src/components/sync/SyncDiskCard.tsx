import { fmtBytes } from '../../lib/format'
import type { SyncDiskInfo } from '../../types/api'

interface SyncDiskCardProps {
  label: string
  info: SyncDiskInfo | null
}

export function SyncDiskCard({ label, info }: SyncDiskCardProps) {
  if (!info) return null

  const usedPct = info.totalSpace > 0
    ? ((info.totalSpace - info.freeSpace) / info.totalSpace) * 100
    : 0

  const barColor = usedPct > 90 ? 'bg-ps-circle' : usedPct > 75 ? 'bg-amber' : 'bg-ps-triangle'

  return (
    <div className="flex-1 p-3 bg-card/60 rounded border border-border/30 xmb-glow">
      <div className="text-[10px] font-medium text-text-2 mb-2 tracking-wide uppercase">{label}</div>
      <div className="space-y-1 text-[10px] text-text-3">
        <div className="flex justify-between">
          <span>ISOs</span>
          <span className="font-mono tabular-nums">{info.isoCount} ({fmtBytes(info.isoTotalSize)})</span>
        </div>
        <div className="flex justify-between">
          <span>Free</span>
          <span className="font-mono tabular-nums">{fmtBytes(info.freeSpace)}</span>
        </div>
        <div className="flex justify-between">
          <span>Total</span>
          <span className="font-mono tabular-nums">{fmtBytes(info.totalSpace)}</span>
        </div>
      </div>
      <div className="mt-2 w-full h-1 bg-surface-3/40 rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${barColor}`} style={{ width: `${usedPct.toFixed(1)}%` }} />
      </div>
      <div className="text-[10px] text-text-4 mt-1 text-right tabular-nums">{usedPct.toFixed(1)}%</div>
    </div>
  )
}
