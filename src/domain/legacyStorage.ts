const OLD_PREFIX = "archforge.";
const NEW_PREFIX = "bondstone.";

/** Lo guardado bajo el nombre viejo pasa al nuevo una sola vez, antes de que nadie lo lea. */
export function migrateLegacyKeys(): void {
  try {
    const storage = window.localStorage;
    for (const key of Object.keys(storage)) {
      if (!key.startsWith(OLD_PREFIX)) continue;
      const value = storage.getItem(key);
      const renamed = NEW_PREFIX + key.slice(OLD_PREFIX.length);
      if (value !== null && storage.getItem(renamed) === null) storage.setItem(renamed, value);
      storage.removeItem(key);
    }
  } catch {
    return;
  }
}
