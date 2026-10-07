export function formatBytes(bytes: number, locale: string): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return new Intl.NumberFormat(locale).format(0);
  }
  const units = ["B", "KB", "MB", "GB", "TB"];
  let v = bytes;
  let u = 0;
  while (v >= 1024 && u < units.length - 1) {
    v /= 1024;
    u += 1;
  }
  const unit = units[u] ?? "B";
  if (u === 0) {
    return `${new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(v)} ${unit}`;
  }
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(v)} ${unit}`;
}

export function formatDate(iso: string, locale: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) {
    return iso;
  }
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(d);
}

export function formatNumber(n: number, locale: string): string {
  return new Intl.NumberFormat(locale).format(n);
}

export function formatPercent(ratio: number, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: "percent",
    maximumFractionDigits: 0,
  }).format(ratio);
}
