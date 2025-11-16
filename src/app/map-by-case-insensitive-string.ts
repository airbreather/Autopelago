const LOCALE_KEY = Symbol.for('locale');

export interface ReadonlyMapByCaseInsensitiveString<Val, Locale extends Intl.LocalesArgument> {
  [LOCALE_KEY]: Locale;
  get(key: string): Val | undefined;
  has(key: string): boolean;
}

export class MapByCaseInsensitiveString<Val, Locale extends Intl.LocalesArgument = 'en'> implements ReadonlyMapByCaseInsensitiveString<Val, Locale> {
  readonly #map = new Map<string, Val>();
  readonly #locale: Locale;

  constructor(locale: Locale) {
    this.#locale = locale;
  }

  get [LOCALE_KEY]() {
    return this.#locale;
  }

  get(key: string): Val | undefined {
    return this.#map.get(key.toLocaleLowerCase(this.#locale));
  }

  has(key: string) {
    return this.#map.has(key.toLocaleLowerCase(this.#locale));
  }

  set(key: string, val: Val) {
    this.#map.set(key.toLocaleLowerCase(this.#locale), val);
  }
}
