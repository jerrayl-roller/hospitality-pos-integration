import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { TabPanelComponent } from './features/tab/tab-panel';

const PANEL_HIDDEN_ROUTES = ['/booking-search', '/admin', '/tabs'];

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    TabPanelComponent
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly router = inject(Router);

  get showTabPanel(): boolean {
    const path = this.router.url.split('?')[0];
    return !PANEL_HIDDEN_ROUTES.includes(path);
  }
}
