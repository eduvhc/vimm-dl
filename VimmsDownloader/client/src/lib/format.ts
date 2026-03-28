export function fmtBytes(bytes: number): string {
  if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + ' GB'
  if (bytes >= 1048576) return (bytes / 1048576).toFixed(2) + ' MB'
  if (bytes >= 1024) return (bytes / 1024).toFixed(2) + ' KB'
  return bytes + ' B'
}

export interface ParsedProgress {
  filename: string
  pct: string
  size: string
  width: string
  speed: string | null
}

export function parseProgress(msg: string): ParsedProgress | null {
  // Format: "filename: 123.45 / 456.78 MB (45.67%) [2.50 MB/s]"
  const m1 = msg.match(/^(.+?):\s+([\d.]+)\s*\/\s*([\d.]+)\s*MB\s*\(([\d.]+)%\)(?:\s*\[([\d.]+)\s*MB\/s\])?/)
  if (m1) {
    return {
      filename: m1[1],
      pct: parseFloat(m1[4]).toFixed(2) + '%',
      size: `${m1[2]} / ${m1[3]} MB`,
      width: parseFloat(m1[4]).toFixed(2) + '%',
      speed: m1[5] ? `${parseFloat(m1[5]).toFixed(2)} MB/s` : null,
    }
  }

  // Format: "filename: 123.45 MB downloaded [2.50 MB/s]"
  const m2 = msg.match(/^(.+?):\s+([\d.]+)\s*MB(?:.*?\[([\d.]+)\s*MB\/s\])?/)
  if (m2) {
    return {
      filename: m2[1],
      pct: '--',
      size: `${m2[2]} MB`,
      width: '0%',
      speed: m2[3] ? `${parseFloat(m2[3]).toFixed(2)} MB/s` : null,
    }
  }

  return null
}

export function parseUrls(text: string): string[] {
  const split = text.replace(/(https?:\/\/)/gi, '\n$1')
  const matches = split.match(/https?:\/\/[^\s]+/gi) || []
  return [...new Set(matches)]
}

export function fmtDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)}s`
  const m = Math.floor(s / 60)
  const rem = s % 60
  return `${m}m ${rem.toFixed(0)}s`
}

export function slugFromUrl(url: string): string {
  const parts = url.replace(/\/$/, '').split('/')
  return decodeURIComponent(parts[parts.length - 1] || url)
}
