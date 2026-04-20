import { Component, inject } from '@angular/core';
import { CommonModule, CurrencyPipe, AsyncPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatDialog } from '@angular/material/dialog';
import { TabStateService, Tab } from '../../core/tab-state.service';
import { NewTabDialogComponent, NewTabResult } from './new-tab-dialog';
import { PreAuthDialogComponent } from '../booking-search/pre-auth-dialog';

@Component({
  selector: 'app-tab-panel',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, AsyncPipe, MatButtonModule, MatIconModule, MatDividerModule, MatProgressSpinnerModule, MatExpansionModule],
  templateUrl: './tab-panel.html',
  styleUrl: './tab-panel.scss'
})
export class TabPanelComponent {
  readonly tabState = inject(TabStateService);
  private readonly dialog = inject(MatDialog);
  openingTab = false;
  discardingChanges = false;

  openNewTabDialog(): void {
    const ref = this.dialog.open(NewTabDialogComponent, { width: '400px', disableClose: true });
    ref.afterClosed().subscribe((result: NewTabResult | null) => {
      if (!result) return;
      this.openingTab = true;
      this.tabState.openNewTab(result).subscribe({
        next: (tab: Tab) => {
          this.openingTab = false;
          this.dialog.open(PreAuthDialogComponent, {
            data: { cardNumber: tab.preAuthCardNumber, cardType: tab.preAuthCardType },
            width: '440px',
            disableClose: true
          });
        },
        error: () => this.openingTab = false
      });
    });
  }

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

  parkTab(): void {
    this.tabState.parkTab();
  }

  onClose(): void {
    if (this.tabState.hasChanges) {
      if (!confirm('Discard changes and close this tab?')) return;
      this.discardChanges();
    } else {
      this.tabState.parkTab();
    }
  }

  discardChanges(): void {
    this.discardingChanges = true;
    this.tabState.discardChanges()?.subscribe({
      next: () => this.discardingChanges = false,
      error: () => this.discardingChanges = false
    });
  }

}
