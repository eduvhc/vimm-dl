export type Tab = 'active' | 'completed' | 'sync' | 'settings'

interface TabBarProps {
  activeTab: Tab
  onTabChange: (tab: Tab) => void
  counts: { active: number; completed: number; sync: number }
}

export function TabBar({ activeTab, onTabChange, counts }: TabBarProps) {
  const tabs: { id: Tab; label: string; count: number }[] = [
    { id: 'active', label: 'Active', count: counts.active },
    { id: 'completed', label: 'Completed', count: counts.completed },
    { id: 'sync', label: 'Sync', count: counts.sync },
    { id: 'settings', label: 'Settings', count: 0 },
  ]

  return (
    <div className="flex items-center gap-0 px-6 bg-surface/50 border-b border-border/30">
      {tabs.map(tab => (
        <button
          key={tab.id}
          onClick={() => onTabChange(tab.id)}
          className={`relative px-5 py-2.5 text-xs font-medium tracking-wide uppercase transition-all ${
            activeTab === tab.id
              ? 'text-accent'
              : 'text-text-4 hover:text-text-3'
          }`}
        >
          {tab.label}
          {tab.count > 0 && (
            <span className={`ml-1.5 font-mono text-[10px] ${
              activeTab === tab.id ? 'text-accent/60' : 'text-text-4/60'
            }`}>
              {tab.count}
            </span>
          )}
          {/* XMB-style active indicator — blue glow bar */}
          {activeTab === tab.id && (
            <div className="absolute bottom-0 left-2 right-2 h-[2px] bg-accent rounded-full
              shadow-[0_0_8px_rgba(91,155,213,0.4)]" />
          )}
        </button>
      ))}
    </div>
  )
}
