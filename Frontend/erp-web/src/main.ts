import { registerLocaleData } from '@angular/common';
import localeEnIn from '@angular/common/locales/en-IN';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

// Indian number formatting for pipes: 1,23,456.00 and the ₹ symbol
registerLocaleData(localeEnIn);

// main.ts bootstraps the ROOT standalone component with the app-wide providers
// (no AppModule: standalone components declare their own imports).
bootstrapApplication(AppComponent, appConfig).catch((err) => console.error(err));
