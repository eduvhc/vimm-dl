type ProgressVariant = 'download' | 'paused' | 'extract' | 'convert' | 'sync' | 'partial'

interface ProgressBarProps {
  width: string
  variant?: ProgressVariant
}

const gradients: Record<ProgressVariant, string> = {
  download: 'bg-gradient-to-r from-ps-cross/80 to-accent/70',
  paused: 'bg-gradient-to-r from-ps-square/60 to-ps-square/40',
  extract: 'bg-amber/60',
  convert: 'bg-ps-cross/60',
  sync: 'bg-accent/60',
  partial: 'bg-amber/40',
}

const glows: Record<ProgressVariant, string> = {
  download: 'shadow-[0_0_8px_rgba(91,155,213,0.3)]',
  paused: '',
  extract: 'shadow-[0_0_6px_rgba(232,163,23,0.2)]',
  convert: 'shadow-[0_0_6px_rgba(46,109,180,0.2)]',
  sync: 'shadow-[0_0_6px_rgba(91,155,213,0.2)]',
  partial: '',
}

export function ProgressBar({ width, variant = 'download' }: ProgressBarProps) {
  return (
    <div className="w-full h-1 bg-surface-3/50 rounded-full overflow-hidden">
      <div
        className={`h-full rounded-full transition-all duration-500 ${gradients[variant]} ${glows[variant]}`}
        style={{ width }}
      />
    </div>
  )
}
