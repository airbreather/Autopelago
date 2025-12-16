import { bootstrapApplication } from '@angular/platform-browser';

import { App } from './app/app';
import { appConfig } from './app/app.config';

CSS.registerProperty({
  name: '--ap-frame-offset',
  syntax: '<number>',
  inherits: true,
  initialValue: '0',
});

CSS.registerProperty({
  name: '--ap-left-base',
  syntax: '<length>',
  inherits: true,
  initialValue: '0',
});

CSS.registerProperty({
  name: '--ap-top-base',
  syntax: '<length>',
  inherits: true,
  initialValue: '0',
});

CSS.registerProperty({
  name: '--ap-scale',
  syntax: '<number>',
  inherits: true,
  initialValue: '4',
});

CSS.registerProperty({
  name: '--ap-neutral-angle',
  syntax: '<angle>',
  inherits: true,
  initialValue: '0rad',
});

CSS.registerProperty({
  name: '--ap-scale-x',
  syntax: '<number>',
  inherits: true,
  initialValue: '1',
});

CSS.registerProperty({
  name: '--ap-wiggle-amount',
  syntax: '<number>',
  inherits: true,
  initialValue: '0',
});

bootstrapApplication(App, appConfig)
  .catch((err: unknown) => {
    console.error(err);
  });
