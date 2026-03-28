import { useEffect, useRef, useState } from 'react'
import { Chart as ChartJS, CategoryScale, LinearScale, PointElement, LineElement, Filler, Tooltip } from 'chart.js'
import { Line } from 'react-chartjs-2'
import { useSettings, useMetrics } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { fmtBytes } from '../../lib/format'
import { Badge } from '../shared/Badge'

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Filler, Tooltip)

const MAX_POINTS = 60 // ~2 min at 2s intervals

function parseSpeed(speed: string | null | undefined): number {
  if (!speed) return 0
  const m = speed.match(/([\d.]+)\s*MB\/s/)
  return m ? parseFloat(m[1]) : 0
}

export function MetricsPanel() {
  const { data: settings } = useSettings()
  const { data: metrics } = useMetrics()
  const { state } = useDownload()

  const [speedHistory, setSpeedHistory] = useState<number[]>([])
  const prevSpeed = useRef<string | null>(null)

  // Push speed data from SignalR progress
  useEffect(() => {
    const raw = state.activeDlInfo?.speed ?? null
    if (raw === prevSpeed.current) return
    prevSpeed.current = raw

    const speed = parseSpeed(raw)
    setSpeedHistory(h => {
      const next = [...h, speed]
      return next.length > MAX_POINTS ? next.slice(-MAX_POINTS) : next
    })
  }, [state.activeDlInfo?.speed])

  // Reset on download stop
  useEffect(() => {
    if (!state.running) setSpeedHistory([])
  }, [state.running])

  const currentSpeed = speedHistory.at(-1) ?? 0
  const avgSpeed = speedHistory.length > 0
    ? speedHistory.reduce((a, b) => a + b, 0) / speedHistory.length : 0

  const diskUsed = metrics ? metrics.diskTotalBytes - metrics.diskFreeBytes : 0
  const diskPct = metrics && metrics.diskTotalBytes > 0
    ? ((diskUsed / metrics.diskTotalBytes) * 100) : 0
  const queuedPct = metrics && metrics.diskFreeBytes > 0
    ? ((metrics.queuedTotalBytes / metrics.diskFreeBytes) * 100) : 0

  const diskBadge = diskPct > 90 ? 'error' as const
    : diskPct > 75 ? 'extracting' as const
    : 'done' as const

  const queueBadge = queuedPct > 80 ? 'error' as const
    : queuedPct > 50 ? 'extracting' as const
    : 'done' as const

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center px-3 sm:px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase">Metrics</span>
      </div>

      <div className="px-3 sm:px-6 py-4 sm:py-5 overflow-y-auto">
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">

          {/* System Info */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">System</div>
            <div className="border border-border/20 rounded p-3 bg-card/30 space-y-2">
              <InfoRow label="Hostname" value={settings?.hostname} />
              <InfoRow label="IPv4" value={settings?.ipv4} />
              <InfoRow label="Platform" value={settings?.platform} />
              <InfoRow label="OS" value={settings?.osDescription} />
              <InfoRow label="Download path" value={settings?.activePath} />
            </div>
          </div>

          {/* Download Speed */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">Download Speed</div>
            <div className="border border-border/20 rounded p-3 bg-card/30">
              <div className="flex items-baseline gap-4 mb-3">
                <div>
                  <span className="text-lg font-mono text-accent tabular-nums">{currentSpeed.toFixed(2)}</span>
                  <span className="text-[10px] text-text-4 ml-1">MB/s</span>
                </div>
                <div>
                  <span className="text-xs font-mono text-text-3 tabular-nums">{avgSpeed.toFixed(2)}</span>
                  <span className="text-[10px] text-text-4 ml-1">avg</span>
                </div>
              </div>
              <div className="h-32">
                <Line
                  data={{
                    labels: speedHistory.map(() => ''),
                    datasets: [{
                      data: speedHistory,
                      borderColor: 'rgba(91, 155, 213, 0.8)',
                      backgroundColor: 'rgba(91, 155, 213, 0.08)',
                      borderWidth: 1.5,
                      pointRadius: 0,
                      fill: true,
                      tension: 0.3,
                    }],
                  }}
                  options={{
                    responsive: true,
                    maintainAspectRatio: false,
                    animation: false,
                    scales: {
                      x: { display: false },
                      y: {
                        beginAtZero: true,
                        ticks: { color: '#3d4d66', font: { size: 9, family: 'monospace' }, callback: v => `${v}` },
                        grid: { color: 'rgba(61, 77, 102, 0.15)' },
                        border: { display: false },
                      },
                    },
                    plugins: { tooltip: { enabled: false } },
                  }}
                />
              </div>
              {!state.running && speedHistory.length === 0 && (
                <div className="text-[10px] text-text-4 text-center mt-2">No active download</div>
              )}
            </div>
          </div>

          {/* Disk Usage */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">Disk Usage</div>
            <div className="border border-border/20 rounded p-3 bg-card/30 space-y-3">
              {/* Disk bar */}
              <div>
                <div className="flex items-center justify-between mb-1">
                  <span className="text-[10px] text-text-4">Volume</span>
                  <Badge variant={diskBadge}>{diskPct.toFixed(1)}% used</Badge>
                </div>
                <div className="h-2 bg-surface-3/50 rounded-full overflow-hidden">
                  <div className="h-full rounded-full bg-accent/50 transition-all"
                    style={{ width: `${Math.min(diskPct, 100)}%` }} />
                </div>
                <div className="flex justify-between text-[10px] text-text-4 mt-1">
                  <span>{fmtBytes(diskUsed)} used</span>
                  <span>{metrics ? fmtBytes(metrics.diskFreeBytes) : '--'} free</span>
                </div>
              </div>

              {/* Queue vs Free */}
              <div className="flex items-center justify-between py-2 border-t border-border/10">
                <div>
                  <div className="text-xs text-text">Queued</div>
                  <div className="text-[10px] text-text-4">
                    {metrics?.queuedCount ?? 0} games &middot; {metrics ? fmtBytes(metrics.queuedTotalBytes) : '--'}
                  </div>
                  <div className="text-[9px] text-text-4/60 mt-0.5">Estimated from metadata — actual size may differ</div>
                </div>
                <Badge variant={queueBadge}>
                  {queuedPct > 0 ? `${queuedPct.toFixed(0)}% of free` : 'OK'}
                </Badge>
              </div>

              {/* Downloading */}
              {metrics && metrics.downloadingCount > 0 && (
                <div className="flex items-center justify-between py-2 border-t border-border/10">
                  <div>
                    <div className="text-xs text-text">Downloading</div>
                    <div className="text-[10px] text-text-4">
                      {metrics.downloadingCount} files &middot; {fmtBytes(metrics.downloadingTotalBytes)}
                    </div>
                    <div className="text-[9px] text-text-4/60 mt-0.5">Partial files — resumes automatically on restart</div>
                  </div>
                  <Badge variant="downloading">In Progress</Badge>
                </div>
              )}

              {/* Completed */}
              <div className="flex items-center justify-between py-2 border-t border-border/10">
                <div>
                  <div className="text-xs text-text">Completed</div>
                  <div className="text-[10px] text-text-4">
                    {metrics?.completedCount ?? 0} files &middot; {metrics ? fmtBytes(metrics.completedTotalBytes) : '--'}
                  </div>
                  <div className="text-[9px] text-text-4/60 mt-0.5">Archives and converted ISOs tracked in the database</div>
                </div>
              </div>

              {/* Orphaned */}
              {metrics && metrics.orphanedCount > 0 && (
                <div className="flex items-center justify-between py-2 border-t border-border/10">
                  <div>
                    <div className="text-xs text-text">Orphaned</div>
                    <div className="text-[10px] text-text-4">
                      {metrics.orphanedCount} files &middot; {fmtBytes(metrics.orphanedTotalBytes)}
                    </div>
                    <div className="text-[9px] text-text-4/60 mt-0.5">Not in database — leftover from a previous install or DB reset</div>
                  </div>
                  <Badge variant="extracting">Untracked</Badge>
                </div>
              )}
            </div>
          </div>

        </div>

      </div>
    </div>
  )
}

function InfoRow({ label, value }: { label: string; value?: string | null }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-[10px] text-text-4">{label}</span>
      <span className="text-[11px] text-text-3 font-mono truncate ml-3 max-w-64 text-right">{value ?? '--'}</span>
    </div>
  )
}
