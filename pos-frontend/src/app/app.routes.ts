import { Routes } from '@angular/router';
import { CatalogueComponent } from './features/catalogue/catalogue';
import { TabsComponent } from './features/tabs/tabs';
import { AdminComponent } from './features/admin/admin';

export const routes: Routes = [
  { path: '', redirectTo: 'catalogue', pathMatch: 'full' },
  { path: 'catalogue', component: CatalogueComponent },
  { path: 'tabs', component: TabsComponent },
  { path: 'admin', component: AdminComponent }
];
