import { useState } from 'react'
import { useVersion } from '../../api/queries'

export function UpdateBanner() {
  const { data: version } = useVersion()
  const [dismissed, setDismissed] = useState(false)

  if (!version?.hasUpdate || dismissed) return null

  return (
    <div className="mx-6 mt-2 flex items-center justify-between p-2 bg-accent/6 border border-accent/15 rounded">
      <span className="text-[10px] text-accent/80 tracking-wide">
        Update available: <span className="font-mono font-medium">{version.latest}</span>
        {version.url && (
          <a href={version.url} target="_blank" rel="noreferrer"
            className="ml-2 underline hover:text-accent">
            Download
          </a>
        )}
      </span>
      <button
        onClick={() => setDismissed(true)}
        className="text-accent/30 hover:text-accent text-sm ml-2"
      >
        &times;
      </button>
    </div>
  )
}
