import { useState } from 'react'
import { useAddToQueue, useSettings } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { parseUrls } from '../../lib/format'

export function Toolbar() {
  const [text, setText] = useState('')
  const addMutation = useAddToQueue()
  const { state, connection } = useDownload()
  const { data: config } = useSettings()

  async function handleAdd() {
    const urls = parseUrls(text)
    if (urls.length === 0) return

    await addMutation.mutateAsync({ urls })
    setText('')

    if (!state.running && connection) {
      connection.invoke('StartDownload', config?.activePath ?? null)
    }
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.ctrlKey && e.key === 'Enter') {
      e.preventDefault()
      handleAdd()
    }
  }

  return (
    <div className="flex gap-3 px-6 py-2.5 border-b border-border/30">
      <textarea
        value={text}
        onChange={e => setText(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="Paste vault URLs here..."
        rows={1}
        className="flex-1 bg-surface/80 border border-border/60 rounded px-3 py-1.5 text-sm text-text
          placeholder:text-text-4 resize-none focus:outline-none focus:border-accent/40 focus:shadow-[0_0_12px_rgba(91,155,213,0.1)]
          transition-all min-h-[32px]"
      />
      {/* PS3 Cross (X) button = confirm/action = blue */}
      <button
        onClick={handleAdd}
        disabled={addMutation.isPending}
        className="px-5 py-1.5 bg-ps-cross/20 text-[#7eb3e0] border border-ps-cross/30 rounded text-sm
          font-medium hover:bg-ps-cross/30 hover:shadow-[0_0_16px_rgba(46,109,180,0.2)]
          transition-all disabled:opacity-40"
      >
        Add
      </button>
    </div>
  )
}
