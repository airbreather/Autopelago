# Angular Guidelines

You are an expert in TypeScript, Angular, and scalable web application development. You write maintainable, performant, and accessible code following Angular and TypeScript best practices.

## General Notes on the Environment
- This project uses `bun` instead of `npm` / `npx` / `node`.
- Command for a one-shot test to make sure everything compiles correctly: `bun run build`.
- The ESLint configuration in this project is maximally strict. Regular use of `bun run lint` is expected. `bun run lint --fix` for issues with auto fixes.
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
    - An example of where it would be overkill is in `game-definitions-store.ts`, where we use it on the result of `YAML.parse`.
    - An example of where it would be impossible is in `archipelago-client.ts`, where it's used to plug a gap in the compiler so that everything that uses the result can be type checked.
- Prefer type inference when the type is obvious.
- Order `import` lines as follows:
  1. Core Angular modules
  2. Immutable.js modules
  3. NgRx modules
  4. RxJS
  5. Core PixiJS
  6. Other Angular modules
  7. Other PixiJS modules
  8. Any other third-party modules (when in doubt, alphabetical)
  9. Modules from the local project

## Angular Best Practices
- Always use standalone components over NgModules (this is the default).
- Use `signal`s for state management.
- Favor using `resource`s (experimental in Angular 19/20) for asynchronous operations where appropriate.
   - Where not appropriate (e.g., real-time streams of events that need to be consumed sequentially instead of only really caring about the latest one), directly using RxJS is still perfectly fine.
- Implement lazy loading for feature routes.
- This project is **ZONELESS**, so be mindful of when explicit change detection may be needed.

## Components
- Keep components small and focused on a single responsibility.
- Use `input()`, `output()`, `viewChild`, etc., functions instead of decorators. *Note: in Angular 20, there are only very few reasons to use decorators on anything other than class definitions.*
- Use `computed()` for derived state.
- Add meaningful accessibility attributes (ARIA) where the browser may otherwise make an incorrect inference.
- Always use inline templates. *Note: most of the time, these will be refactored later, but keeping it together lets us get up and running more quickly.*

## State Management
- Use `signal`s for local component state.
- Consider using NgRx Signals for more global or cross-cutting state.
- Keep state transformations pure and predictable.

## Templates
- Keep templates simple and avoid complex logic.
- Use structural directives (`@if`, `@for`) instead of `*ngIf`, `*ngFor`.
- Use the `async` pipe to handle observables (but remember, most of the time we should be using `resource`s instead).
- Use Angular's global error handler for unhandled errors.
- Implement proper error boundaries for async operations.
- Consider user-friendly error messages for connection failures.

## Performance
- When writing a @for loop, it is preferable to add extra complexity to make a good `track` expression than to have Angular rebuild too much of the DOM because of a poorly written one.
- Implement virtual scrolling for large lists.

## Services
- Design services around a single responsibility.
- Use the `providedIn: 'root'` option for singleton services.
- Use the `inject()` function instead of constructor injection.

## Testing
- Do not add any automated testing at this time.
