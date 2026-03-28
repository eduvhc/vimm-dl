import { useState } from 'react'
import { useEvents } from '../../api/queries'
import { useSettings } from '../../api/queries'
import { EventItem } from './EventItem'

interface FilterDef {
  label: string
  value: string
  flag?: 'featureSync'
}

const FILTERS: FilterDef[] = [
  { label: 'All', value: '' },
  { label: 'Status', value: 'download_status' },
  { label: 'Progress', value: 'download_progress' },
  { label: 'Downloads', value: 'download_completed' },
  { label: 'Errors', value: 'download_error' },
  { label: 'Pipeline', value: 'pipeline_status' },
  { label: 'Sync', value: 'sync', flag: 'featureSync' },
]

const PAGE_SIZE = 100

export function EventsPanel() {
  const [filter, setFilter] = useState('')
  const [limit, setLimit] = useState(PAGE_SIZE)
  const { data: settings } = useSettings()
  const { data } = useEvents(filter || undefined, undefined, limit)

  const events = data?.events ?? []
  const total = data?.total ?? 0
  const hasMore = events.length < total

  const visibleFilters = FILTERS.filter(f => {
    if (!f.flag) return true
    return settings?.[f.flag] ?? false
  })

  function handleFilterChange(value: string) {
    setFilter(value)
    setLimit(PAGE_SIZE)
  }

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between gap-2 px-3 sm:px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase shrink-0">
          {events.length} of {total} events
        </span>
        <div className="flex items-center gap-1 overflow-x-auto scrollbar-none">
          {visibleFilters.map(f => (
            <button
              key={f.value}
              onClick={() => handleFilterChange(f.value)}
              className={`px-2.5 py-1 text-[10px] tracking-wide uppercase rounded transition-colors ${
                filter === f.value
                  ? 'bg-accent/15 text-accent border border-accent/30'
                  : 'text-text-4 hover:text-text-3 border border-transparent'
              }`}
            >
              {f.label}
            </button>
          ))}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto">
        {events.map(e => (
          <EventItem key={e.id} event={e} />
        ))}
        {hasMore && (
          <button
            onClick={() => setLimit(l => l + PAGE_SIZE)}
            className="w-full py-3 text-[10px] text-accent/60 hover:text-accent tracking-wide uppercase
              transition-colors border-t border-border/10"
          >
            Load more ({total - events.length} remaining)
          </button>
        )}
        {events.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
            No events recorded
          </div>
        )}
      </div>
    </div>
  )
}
