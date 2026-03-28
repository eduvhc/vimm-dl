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
      <div className="flex items-center px-3 sm:px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase">Settings</span>
      </div>
      <div className="px-3 sm:px-6 py-4 sm:py-5 overflow-y-auto">
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
          {/* PS3 */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              PS3
            </div>
            <div className="space-y-3 border border-border/20 rounded p-3 bg-card/30">
              <div className="flex items-center justify-between">
                <div>
                  <div className="text-xs text-text">Default format</div>
                  <div className="text-[10px] text-text-4">Format used when adding PS3 games</div>
                </div>
                <select
                  value={settings.ps3DefaultFormat}
                  onChange={e => saveMutation.mutate({ key: 'ps3_default_format', value: e.target.value })}
                  className="bg-surface-2/60 border border-border/40 text-text-3 text-xs rounded px-2 py-1
                    focus:outline-none focus:border-accent/30"
                >
                  <option value="0">JB Folder (.7z)</option>
                  <option value="1">.dec.iso</option>
                </select>
              </div>
              <Toggle
                label="Preserve archive"
                description="Keep the .7z file after conversion"
                checked={settings.ps3PreserveArchive}
                onChange={() => toggle('ps3_preserve_archive', settings.ps3PreserveArchive)}
              />
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

              {/* ISO Rename — subsection */}
              <div className="pt-2 border-t border-border/10">
                <div className="flex items-center gap-2 mb-2">
                  <span className="text-[10px] text-text-3 tracking-wide uppercase">ISO Rename</span>
                  <span className="text-[9px] px-1.5 py-0.5 rounded bg-accent/10 text-accent/70 border border-accent/20">
                    .dec.iso only
                  </span>
                </div>
                <div className="text-[10px] text-text-4 mb-2">
                  JB Folder conversions use PARAM.SFO metadata for naming.
                </div>
                <div className="space-y-1">
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
              </div>
            </div>
            <div className="mt-2 text-[10px] text-text-4">
              Parallelism requires restart. Higher values use more CPU and disk I/O.
            </div>
          </div>

          {/* Feature Flags */}
          <div>
            <div className="text-[10px] text-text-3 tracking-wide uppercase mb-3">
              Feature Flags
            </div>
            <div className="space-y-1 border border-border/20 rounded p-3 bg-card/30">
              <Toggle
                label="Sync (Beta)"
                description="Enable the Sync tab for copying ISOs to an external drive"
                checked={settings.featureSync}
                onChange={() => toggle('feature_sync', settings.featureSync)}
              />
              <Toggle
                label="Event Audit (Developer)"
                description="Enable the Events tab showing the full event log"
                checked={settings.featureEvents}
                onChange={() => toggle('feature_events', settings.featureEvents)}
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
