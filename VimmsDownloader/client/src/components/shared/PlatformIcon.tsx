import { getPlatformIcon } from '../../lib/platformIcons'

interface PlatformIconProps {
  platform: string | null
  className?: string
}

export function PlatformIcon({ platform, className = '' }: PlatformIconProps) {
  const iconUrl = getPlatformIcon(platform)

  if (!iconUrl) {
    return platform ? (
      <span className={`text-[10px] text-text-3 font-medium uppercase ${className}`}>
        {platform}
      </span>
    ) : null
  }

  return (
    <span
      className={`inline-block w-4 h-4 bg-text-3 ${className}`}
      style={{
        maskImage: `url(${iconUrl})`,
        maskSize: 'contain',
        maskRepeat: 'no-repeat',
        maskPosition: 'center',
        WebkitMaskImage: `url(${iconUrl})`,
        WebkitMaskSize: 'contain',
        WebkitMaskRepeat: 'no-repeat',
        WebkitMaskPosition: 'center',
      }}
      title={platform ?? undefined}
    />
  )
}
