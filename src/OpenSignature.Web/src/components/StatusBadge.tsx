interface StatusBadgeProps {
  status: string
}

export function StatusBadge({ status }: StatusBadgeProps) {
  const tone = statusTone(status)
  return <span className={`status-badge tone-${tone}`}>{status}</span>
}

function statusTone(status: string): string {
  switch (status) {
    case 'Completed':
      return 'ok'
    case 'Failed':
      return 'bad'
    case 'Cancelled':
      return 'muted'
    case 'Processing':
      return 'live'
    case 'Queued':
      return 'wait'
    default:
      return 'neutral'
  }
}
