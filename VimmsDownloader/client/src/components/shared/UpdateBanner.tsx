import { useState } from 'react'
import { useVersion } from '../../api/queries'

const REPO_URL = 'https://github.com/eduvhc/vimm-dl'
const RELEASES_URL = `${REPO_URL}/releases`
const UPDATE_GUIDE_URL = `${REPO_URL}/blob/main/UPDATE.md`

export function UpdateBanner() {
  const { data: version } = useVersion()
  const [dismissed, setDismissed] = useState(false)

  if (!version?.hasUpdate || dismissed) return null

  return (
    <div className="mx-3 sm:mx-6 mt-2 p-2.5 sm:p-3 bg-accent/6 border border-accent/15 rounded space-y-1.5">
      <div className="flex items-start justify-between gap-2">
        <span className="text-xs text-accent/90 font-medium leading-relaxed">
          v{version.latest} available
          <span className="text-text-4 font-normal ml-1">(current: v{version.current})</span>
        </span>
        <button
          onClick={() => setDismissed(true)}
          className="text-accent/30 hover:text-accent text-sm ml-2 shrink-0"
        >&times;</button>
      </div>

      <div className="flex items-center gap-2 sm:gap-3 text-[10px] flex-wrap">
        <a href={UPDATE_GUIDE_URL} target="_blank" rel="noreferrer"
          className="text-accent/70 hover:text-accent underline">
          How to update
        </a>
        {version.url && (
          <a href={version.url} target="_blank" rel="noreferrer"
            className="text-accent/70 hover:text-accent underline">
            Changelog
          </a>
        )}
        <a href={RELEASES_URL} target="_blank" rel="noreferrer"
          className="text-accent/70 hover:text-accent underline">
          All releases
        </a>
      </div>

      <div className="text-[10px] text-amber/70 leading-relaxed">
        Docker: bind <span className="font-mono text-amber/90">/vimms</span> to persist your data.
        {' '}Without a volume mount, database and downloads are lost on container update.
      </div>
    </div>
  )
}
