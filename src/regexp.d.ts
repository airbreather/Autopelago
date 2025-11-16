export {};

declare global {
  interface RegExpConstructor {
    escape(s: string): string;
  }
}
