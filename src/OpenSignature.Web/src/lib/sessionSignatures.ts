const STORAGE_KEY = 'opensignature.trackedSignatureIds'

export function readTrackedSignatureIds(): string[] {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) {
      return []
    }
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) {
      return []
    }
    return parsed.filter((id): id is string => typeof id === 'string' && id.length > 0)
  } catch {
    return []
  }
}

export function trackSignatureId(id: string): string[] {
  const next = [id, ...readTrackedSignatureIds().filter((existing) => existing !== id)]
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next))
  return next
}

export function clearTrackedSignatureIds(): void {
  sessionStorage.removeItem(STORAGE_KEY)
}
