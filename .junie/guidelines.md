# Angular Guidelines

You are an expert in TypeScript, Angular, and scalable web application development. You write maintainable, performant, and accessible code following Angular and TypeScript best practices.

## TypeScript Best Practices
- Use strict type checking
- Prefer type inference when the type is obvious
- Avoid the `any` type; use `unknown` when type is uncertain
- Follow these and all other rules according to the ESLint configuration

## Angular Best Practices
- Always use standalone components over NgModules (this is the default)
- Use signals for state management
- Use resources (new in Angular 19) for event handling and asynchronous operations. *Note: the relevant APIs are all currently labeled as experimental, but this is clearly the direction that the ecosystem is going, so a bit of early turbulence is an acceptable tradeoff.*
- Implement lazy loading for feature routes
- Use NgOptimizedImage for all static images

## Components
- Keep components small and focused on a single responsibility
- Use input(), output(), viewChild, etc., functions instead of decorators. *Note: in Angular 20, there are only very few reasons to use decorators on anything other than class definitions.*
- Use computed() for derived state
- Implement OnPush change detection strategy
- Add meaningful accessibility attributes (ARIA) where the browser may otherwise make an incorrect inference
- Always use inline templates. *Note: most of the time, these will be refactored later, but keeping it together lets us get up and running more quickly.*

## State Management
- Use signals for local component state
- Use computed() for derived state
- Consider NgRx Signals for complex application state
- Avoid overusing services as global state containers
- Keep state transformations pure and predictable

## Templates
- Keep templates simple and avoid complex logic
- Use structural directives (@if, @for) instead of *ngIf, *ngFor
- Use the async pipe to handle observables (but remember, most of the time we should be using resources instead).
- Implement proper error handling for async operations

## Services
- Design services around a single responsibility
- Use the providedIn: 'root' option for singleton services
- Use the inject() function instead of constructor injection