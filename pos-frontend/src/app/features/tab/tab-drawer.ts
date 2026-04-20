import { Component, inject } from '@angular/core';
import { CommonModule, CurrencyPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatDividerModule } from '@angular/material/divider';
import { TabStateService } from '../../core/tab-state.service';

@Component({
  selector: 'app-tab-drawer',
  standalone: true,
  imports: [
    CommonModule,
    CurrencyPipe,
    MatButtonModule,
    MatIconModule,
    MatTableModule,
    MatDividerModule
  ],
  templateUrl: './tab-drawer.html',
  styleUrl: './tab-drawer.scss'
})
export class TabDrawerComponent {
  readonly tabState = inject(TabStateService);
  readonly displayedColumns = ['name', 'qty', 'price', 'total', 'actions'];
  closingTab = false;

  increment(productId: string, productName: string, unitPrice: number): void {
    const tab = this.tabState.currentTab;
    if (!tab) return;
    this.tabState.addItem(tab.tabId, { productId, productName, quantity: 1, unitPrice }).subscribe();
  }

  decrement(productId: string): void {
    const tab = this.tabState.currentTab;
    if (!tab) return;
    this.tabState.removeItem(tab.tabId, productId).subscribe();
  }

  closeTab(): void {
    const tab = this.tabState.currentTab;
    if (!tab) return;
    this.closingTab = true;
    this.tabState.closeTab(tab.tabId).subscribe({
      next: () => this.closingTab = false,
      error: () => this.closingTab = false
    });
  }

  get canClose(): boolean {
    const tab = this.tabState.currentTab;
    return !!tab && tab.addedItems.length === 0;
  }
}
