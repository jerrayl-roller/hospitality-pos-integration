import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatDividerModule } from '@angular/material/divider';
import { ApiService } from '../../core/api.service';
import { TabStateService } from '../../core/tab-state.service';

export interface TabSummary {
  tabId: string;
  bookingUniqueId: string | null;
  bookingReference: string | null;
  guestName: string | null;
  itemCount: number;
  grandTotal: number;
  amountRemaining: number;
  preAuthCardType: string | null;
  preAuthCardLast4: string | null;
  paymentStatus: string;
  openedAt: string;
}

interface ReceiptLineItem {
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  gstAmount: number;
}

interface ReceiptPayment {
  method: string;
  reference: string | null;
  amount: number;
  isTip: boolean;
}

interface ReceiptData {
  tabId: string;
  receiptNumber: string;
  venueName: string;
  abnPlaceholder: string;
  issuedAt: string;
  guestName: string | null;
  lineItems: ReceiptLineItem[];
  subtotalExclGst: number;
  gstTotal: number;
  grandTotal: number;
  tipTotal: number;
  payments: ReceiptPayment[];
}

@Component({
  selector: 'app-tabs',
  standalone: true,
  imports: [
    CommonModule,
    CurrencyPipe,
    DatePipe,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatSidenavModule,
    MatDividerModule
  ],
  templateUrl: './tabs.html',
  styleUrl: './tabs.scss'
})
export class TabsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly tabState = inject(TabStateService);

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly tabs = signal<TabSummary[]>([]);
  readonly loadingTabId = signal<string | null>(null);

  readonly receiptTabId = signal<string | null>(null);
  readonly receipt = signal<ReceiptData | null>(null);
  readonly receiptLoading = signal(false);
  readonly receiptError = signal<string | null>(null);
  readonly retrySyncingTabId = signal<string | null>(null);

  readonly columns = ['status', 'guest', 'card', 'opened', 'items', 'total', 'amountDue', 'actions'];

  ngOnInit(): void {
    this.load();
    const receiptTabId = this.route.snapshot.queryParamMap.get('receipt');
    if (receiptTabId) {
      this.openReceipt(receiptTabId);
    }
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
      next: () => { this.loadingTabId.set(null); this.router.navigate(['/catalogue']); },
      error: () => this.loadingTabId.set(null)
    });
  }

  openReceipt(tabId: string): void {
    this.receiptTabId.set(tabId);
    this.receipt.set(null);
    this.receiptLoading.set(true);
    this.receiptError.set(null);
    this.api.get<ReceiptData>(`/api/tabs/${tabId}/receipt`).subscribe({
      next: data => { this.receipt.set(data); this.receiptLoading.set(false); },
      error: () => { this.receiptError.set('Could not load receipt.'); this.receiptLoading.set(false); }
    });
  }

  closeReceipt(): void {
    this.receiptTabId.set(null);
    this.receipt.set(null);
  }

  retrySync(tabId: string): void {
    this.retrySyncingTabId.set(tabId);
    this.api.post<TabSummary>(`/api/tabs/${tabId}/retry-sync`, {}).subscribe({
      next: () => { this.retrySyncingTabId.set(null); this.load(); },
      error: () => this.retrySyncingTabId.set(null)
    });
  }

  receiptTabStatus(): string | null {
    const tabId = this.receiptTabId();
    if (!tabId) return null;
    return this.tabs().find(t => t.tabId === tabId)?.paymentStatus ?? null;
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
