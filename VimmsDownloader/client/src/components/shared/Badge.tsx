type BadgeVariant = 'downloading' | 'paused' | 'queued' | 'starting' |
  'extracting' | 'converting' | 'done' | 'error' | 'skipped' | 'waiting'

interface BadgeProps {
  variant: BadgeVariant
  children: React.ReactNode
}

const variantStyles: Record<BadgeVariant, string> = {
  downloading: 'bg-ps-triangle/15 text-ps-triangle border-ps-triangle/20 shadow-[0_0_8px_rgba(0,166,81,0.1)]',
  paused: 'bg-ps-square/15 text-[#c49be0] border-ps-square/20',
  queued: 'bg-surface-3/50 text-text-4 border-border/30',
  starting: 'bg-ps-cross/15 text-[#7eb3e0] border-ps-cross/20',
  extracting: 'bg-amber/12 text-amber border-amber/20',
  converting: 'bg-ps-cross/15 text-[#7eb3e0] border-ps-cross/20',
  done: 'bg-ps-triangle/12 text-ps-triangle border-ps-triangle/20',
  error: 'bg-ps-circle/12 text-[#e06070] border-ps-circle/20',
  skipped: 'bg-surface-3/30 text-text-4 border-border/20',
  waiting: 'bg-surface-3/40 text-text-3 border-border/30',
}

export function Badge({ variant, children }: BadgeProps) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 text-[10px] font-medium
      rounded border tracking-wide ${variantStyles[variant]}`}>
      {children}
    </span>
  )
}
