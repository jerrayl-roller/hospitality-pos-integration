import { Component, inject, ViewChild } from '@angular/core';
import { CommonModule, AsyncPipe } from '@angular/common';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MatSidenavModule, MatDrawer } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TabStateService } from './core/tab-state.service';
import { TabDrawerComponent } from './features/tab/tab-drawer';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    AsyncPipe,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatToolbarModule,
    MatListModule,
    MatButtonModule,
    MatIconModule,
    TabDrawerComponent
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  readonly tabState = inject(TabStateService);
  @ViewChild('tabDrawer') tabDrawer!: MatDrawer;
  openingTab = false;

  openNewTab(): void {
    this.openingTab = true;
    this.tabState.openNewTab().subscribe({
      next: () => {
        this.openingTab = false;
        this.tabDrawer.open();
      },
      error: () => {
        this.openingTab = false;
      }
    });
  }

  toggleTabDrawer(): void {
    this.tabDrawer.toggle();
  }
}
