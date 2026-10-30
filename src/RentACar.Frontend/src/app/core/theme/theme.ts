import { EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';
import { TemaServisi } from './theme-service';

/** Saklı tema tercihi ilk çizimden önce `<html data-theme>`'e yazılır. */
export function provideTheme(): EnvironmentProviders {
  return provideAppInitializer(() => {
    inject(TemaServisi);
  });
}
