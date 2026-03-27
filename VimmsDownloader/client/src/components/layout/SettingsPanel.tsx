import { useSettings, useSaveSetting } from '../../api/queries'

interface ToggleProps {
  label: string
  description: string
  checked: boolean
  onChange: (checked: boolean) => void
}

function Toggle({ label, description, checked, onChange }: ToggleProps) {
  return (
    <label className="flex items-center justify-between py-2 cursor-pointer group">
      <div>
        <div className="text-xs text-text">{label}</div>
        <div className="text-[10px] text-text-4">{description}</div>
      </div>
      <button
        onClick={() => onChange(!checked)}
        className={`relative w-8 h-4 rounded-full transition-colors ${
          checked ? 'bg-accent/40' : 'bg-surface-3'
        }`}
      >
        <div className={`absolute top-0.5 w-3 h-3 rounded-full transition-all ${
          checked
            ? 'left-4 bg-accent shadow-[0_0_6px_rgba(91,155,213,0.3)]'
            : 'left-0.5 bg-text-4'
        }`} />
      </button>
    </label>
  )
}

export function SettingsPanel() {
  const { data: settings } = useSettings()
  const saveMutation = useSaveSetting()

  if (!settings) return null

  function toggle(key: string, current: boolean) {
    saveMutation.mutate({ key, value: (!current).toString() })
  }

  function setParallelism(value: number) {
    const clamped = Math.max(1, Math.min(8, value))
    saveMutation.mutate({ key: 'ps3_parallelism', value: clamped.toString() })
  }

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase">Settings</span>
      </div>
      <div className="px-6 py-3 max-w-lg space-y-5 overflow-y-auto">
        {/* ISO Rename */}
        <div>
          <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
            ISO Rename Rules
          </div>
          <div className="space-y-1 border border-border/20 rounded p-3 bg-card/30">
            <Toggle
              label="Fix 'The' placement"
              description={`"Godfather, The" → "The Godfather"`}
              checked={settings.fixThe}
              onChange={() => toggle('rename_fix_the', settings.fixThe)}
            />
            <Toggle
              label="Add serial number"
              description={`Append serial: "Game - BLES-00043.iso"`}
              checked={settings.addSerial}
              onChange={() => toggle('rename_add_serial', settings.addSerial)}
            />
            <Toggle
              label="Strip region"
              description={`Remove "(Europe)", "(USA)" from filename`}
              checked={settings.stripRegion}
              onChange={() => toggle('rename_strip_region', settings.stripRegion)}
            />
          </div>
          <div className="mt-2 text-[10px] text-text-4">
            Only applies to .dec.iso downloads. JB Folder conversions use PARAM.SFO metadata for naming.
          </div>
        </div>

        {/* PS3 Pipeline */}
        <div>
          <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
            PS3 Conversion Pipeline
          </div>
          <div className="border border-border/20 rounded p-3 bg-card/30">
            <div className="flex items-center justify-between">
              <div>
                <div className="text-xs text-text">Max parallelism</div>
                <div className="text-[10px] text-text-4">Workers per phase (extract + convert)</div>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setParallelism(settings.ps3Parallelism - 1)}
                  disabled={settings.ps3Parallelism <= 1}
                  className="w-6 h-6 flex items-center justify-center rounded
                    bg-surface-3/50 text-text-3 hover:bg-surface-3 hover:text-text
                    disabled:opacity-30 text-xs"
                >-</button>
                <span className="text-sm font-mono text-accent w-4 text-center tabular-nums">
                  {settings.ps3Parallelism}
                </span>
                <button
                  onClick={() => setParallelism(settings.ps3Parallelism + 1)}
                  disabled={settings.ps3Parallelism >= 8}
                  className="w-6 h-6 flex items-center justify-center rounded
                    bg-surface-3/50 text-text-3 hover:bg-surface-3 hover:text-text
                    disabled:opacity-30 text-xs"
                >+</button>
              </div>
            </div>
          </div>
          <div className="mt-2 text-[10px] text-text-4">
            Requires restart to take effect. Higher values use more CPU and disk I/O.
          </div>
        </div>
      </div>
    </div>
  )
}
