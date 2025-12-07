# Angular Guidelines

You are an expert in TypeScript, Angular, and scalable web application development. You write maintainable, performant, and accessible code following Angular and TypeScript best practices.

## General Notes on the Environment
- This project uses `bun` instead of `npm` / `npx` / `node`.
- Command for a one-shot test to make sure everything compiles correctly: `bun run build`.
- The ESLint configuration in this project is maximally strict. Regular use of `bun run lint` is expected. `bun run lint --fix` for issues with auto fixes.
  - This is because the project is developed and maintained by a team of developers who are abnormally proficient in TypeScript, and so, e.g., not only do we forbid `any` in our own APIs (normal), but an `any` coming from a third-party API must be cast to `unknown` and then narrowed down with specific runtime checks for whatever we want to do with it (strict).
- Whenever these exact shell commands solve a problem for you, run them exactly as spelled below instead of varying them (especially avoid prefixing with `cd /path/to/project &&`) as these exact spellings are the only shell commands that you are allowed to run without asking for permission:
  1. `bun run build`
  2. `bun run lint`
  3. `bun run lint --fix`
  4. `git diff`

## TypeScript Best Practices
- Use strict type checking:
  1. NEVER use the `any` type. There is ALWAYS an alternative.
  2. When a type is truly uncertain, use `unknown` and have the compiler guide you to making the correct runtime type checks.
  3. Cast sparingly (stylistically: using `as`, never `<>`) and only when runtime type checking would be impossible or overkill.
- Prefer type inference when the type is obvious.

## Angular Best Practices
- Always use standalone components over NgModules (this is the default).
- Use `signal`s for state management.
- Favor using `resource`s (experimental in Angular 19/20/21) for asynchronous operations where appropriate.
   - Where not appropriate (e.g., real-time streams of events that need to be consumed sequentially instead of only really caring about the latest one), directly using RxJS is still perfectly fine.
- Do not do any lazy-loading. Ideally, the whole application should load right away. This is in part because the PWA service worker will download everything anyway and in part because the whole thing is so small (all static files zipped up: <2 MiB with everything, <600 KiB when removing source maps).
- This project is **ZONELESS**. That probably won't affect you because you're using signals already.

## Components
- Keep components small and focused on a single responsibility.
- Use `input()`, `output()`, `viewChild`, etc., functions instead of decorators. *Note: in Angular 21, there are only very few reasons to use decorators on anything other than class definitions.*
- Use `computed()` for derived state.
- Add meaningful accessibility attributes (ARIA) where the browser may otherwise make an incorrect inference.
- Always use inline templates and styles. The original plan was to refactor large ones later, but the developers on this project have found it much easier to work with all the bits of a component in one file. When things get too big for a single file, it's been better to split out an entire child component.
- When following all the other best-practices, you should have no issues with every component being `OnPush` as well. No cop-outs.

## State Management
- Use `signal`s for local component state.
- Consider using NgRx Signals for more global or cross-cutting state.
- Keep state transformations pure and predictable.

## Templates
- Avoid overly complex logic in the template itself. `computed` signals generally perform better (practically never worse), and they're usually easier to use anyway.
- Use control flow syntax (`@if`, `@for`) instead of structural directives `*ngIf`, `*ngFor`.
- Use the `async` pipe to handle observables (but remember, most of the time we should be using `resource`s instead).
- Use Angular's global error handler for unhandled errors.
- Implement proper error boundaries for async operations.
- Consider user-friendly error messages for connection failures.

## Performance
- When writing a `@for` loop, it is preferable to add extra complexity to make a good `track` expression than to have Angular rebuild too much of the DOM because of a poorly written one.
- Implement virtual scrolling for large lists.

## Services
- Design services around a single responsibility.
- Use the `providedIn: 'root'` option for singleton services.
- Use the `inject()` function instead of constructor injection.

## Testing
- Do not add any automated testing at this time.
