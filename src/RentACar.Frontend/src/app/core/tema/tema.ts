import { EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';
import { TemaServisi } from './tema-servisi';

/** Saklı tema tercihi ilk çizimden önce `<html data-theme>`'e yazılır. */
export function provideTema(): EnvironmentProviders {
  return provideAppInitializer(() => {
    inject(TemaServisi);
  });
}
