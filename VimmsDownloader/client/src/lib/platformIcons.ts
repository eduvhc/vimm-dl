const platformMap: Record<string, string> = {
  'playstation 3': 'playstation3',
  'playstation 2': 'playstation2',
  'playstation': 'playstation',
  'psp': 'psp',
  'ps vita': 'psvita',
  'nintendo 64': 'n64',
  'gamecube': 'gamecube',
  'wii': 'wii',
  'wii u': 'wiiu',
  'switch': 'switch',
  'game boy': 'gameboy',
  'game boy color': 'gameboycolor',
  'game boy advance': 'gameboyadvance',
  'nintendo ds': 'nds',
  'nintendo 3ds': '3ds',
  'nes': 'nes',
  'super nintendo': 'snes',
  'sega genesis': 'genesis',
  'sega saturn': 'saturn',
  'dreamcast': 'dreamcast',
  'sega master system': 'mastersystem',
  'sega game gear': 'gamegear',
  'xbox': 'xbox',
  'xbox 360': 'xbox360',
  'turbografx-16': 'turbografx16',
  'neo geo': 'neogeo',
}

export function getPlatformIcon(platform: string | null): string | null {
  if (!platform) return null
  const key = platform.toLowerCase()
  const file = platformMap[key]
  return file ? `/icons/${file}.svg` : null
}
