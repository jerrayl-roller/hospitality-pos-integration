import { Routes } from '@angular/router';
import { CatalogueComponent } from './features/catalogue/catalogue';

export const routes: Routes = [
  { path: '', redirectTo: 'catalogue', pathMatch: 'full' },
  { path: 'catalogue', component: CatalogueComponent }
];
