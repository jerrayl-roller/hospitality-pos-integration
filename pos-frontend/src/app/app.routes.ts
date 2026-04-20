import { Routes } from '@angular/router';
import { CatalogueComponent } from './features/catalogue/catalogue';
import { TabsComponent } from './features/tabs/tabs';
import { AdminComponent } from './features/admin/admin';
import { BookingSearchComponent } from './features/booking-search/booking-search';
import { ReceiptComponent } from './features/receipt/receipt';

export const routes: Routes = [
  { path: '', redirectTo: 'catalogue', pathMatch: 'full' },
  { path: 'catalogue', component: CatalogueComponent },
  { path: 'booking-search', component: BookingSearchComponent },
  { path: 'tabs', component: TabsComponent },
  { path: 'admin', component: AdminComponent },
  { path: 'receipt/:tabId', component: ReceiptComponent }
];
