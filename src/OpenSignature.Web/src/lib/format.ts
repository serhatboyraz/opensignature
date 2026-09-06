export function isTerminalStatus(status: string | undefined): boolean {
  if (!status) {
    return false
  }
  return status === 'Completed' || status === 'Failed' || status === 'Cancelled'
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }
  return date.toLocaleString()
}

export function shortId(id: string): string {
  if (id.length <= 12) {
    return id
  }
  return `${id.slice(0, 8)}…${id.slice(-4)}`
}
