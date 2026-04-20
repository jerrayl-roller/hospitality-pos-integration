import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { ApiService } from '../../core/api.service';
import { TabStateService, Tab } from '../../core/tab-state.service';

export interface TabSummary {
  tabId: string;
  bookingId: string | null;
  guestName: string | null;
  itemCount: number;
  grandTotal: number;
  amountRemaining: number;
  preAuthCardType: string | null;
  preAuthCardLast4: string | null;
  paymentStatus: string;
  openedAt: string;
}

@Component({
  selector: 'app-tabs',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, MatTableModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatChipsModule],
  templateUrl: './tabs.html',
  styleUrl: './tabs.scss'
})
export class TabsComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly tabState = inject(TabStateService);

  loading = signal(true);
  error = signal(false);
  tabs = signal<TabSummary[]>([]);
  loadingTabId = signal<string | null>(null);

  readonly columns = ['status', 'guest', 'card', 'opened', 'items', 'total', 'amountDue', 'actions'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.api.get<TabSummary[]>('/api/tabs').subscribe({
      next: tabs => { this.tabs.set(tabs); this.loading.set(false); },
      error: () => { this.error.set(true); this.loading.set(false); }
    });
  }

  switchToTab(summary: TabSummary): void {
    this.loadingTabId.set(summary.tabId);
    this.tabState.refreshTab(summary.tabId).subscribe({
      next: () => this.loadingTabId.set(null),
      error: () => this.loadingTabId.set(null)
    });
  }

  isActive(tabId: string): boolean {
    return this.tabState.currentTab?.tabId === tabId;
  }

  cardLabel(cardType: string | null): string {
    switch (cardType) {
      case 'visa': return 'VISA';
      case 'mastercard': return 'MC';
      case 'amex': return 'AMEX';
      default: return 'CARD';
    }
  }
}
