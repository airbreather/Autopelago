export function strictObjectEntries<T extends object>(obj: T): [keyof T, T[keyof T]][] {
  return Object.entries(obj) as [keyof T, T[keyof T]][];
}

export function stricterObjectFromEntries<T extends object, V>(entries: [k: keyof T, v: V][]): Record<keyof T, V> {
  return Object.fromEntries(entries) as Record<keyof T, V>;
}
