import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { ApiService } from '../../core/api.service';

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
  selector: 'app-receipt',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatDividerModule],
  template: `
    <div class="receipt-page">
      @if (loading()) {
        <div class="loading-state">
          <mat-spinner diameter="48"></mat-spinner>
          <p>Loading receipt…</p>
        </div>
      } @else if (error()) {
        <div class="error-state">
          <mat-icon>error_outline</mat-icon>
          <p>{{ error() }}</p>
          <button mat-raised-button color="primary" (click)="newTab()">Back to Catalogue</button>
        </div>
      } @else if (receipt()) {
        <div class="receipt-wrapper">
          <div class="receipt">

            <div class="receipt-header">
              <div class="receipt-title">TAX INVOICE</div>
              <div class="venue-name">{{ receipt()!.venueName }}</div>
              <div class="receipt-meta">ABN: {{ receipt()!.abnPlaceholder }}</div>
              <div class="receipt-meta">{{ receipt()!.issuedAt | date:'d MMM yyyy, h:mm a' }}</div>
              <div class="receipt-meta receipt-number">Receipt #{{ receipt()!.receiptNumber }}</div>
              @if (receipt()!.guestName) {
                <div class="receipt-meta">Guest: {{ receipt()!.guestName }}</div>
              }
            </div>

            <div class="receipt-rule"></div>

            <div class="items-header">
              <span class="col-desc">DESCRIPTION</span>
              <span class="col-qty">QTY</span>
              <span class="col-unit">UNIT</span>
              <span class="col-total">TOTAL</span>
            </div>

            <div class="receipt-rule thin"></div>

            @for (item of receipt()!.lineItems; track item.name) {
              <div class="item-line">
                <span class="col-desc item-name-col">{{ item.name }}</span>
                <span class="col-qty">{{ item.quantity }}</span>
                <span class="col-unit">{{ item.unitPrice | currency:'AUD':'symbol':'1.2-2' }}</span>
                <span class="col-total">{{ item.lineTotal | currency:'AUD':'symbol':'1.2-2' }}</span>
              </div>
            }

            @if (receipt()!.lineItems.length === 0) {
              <div class="no-items">No items</div>
            }

            <div class="receipt-rule"></div>

            <div class="total-line">
              <span>Subtotal (excl. GST)</span>
              <span>{{ receipt()!.subtotalExclGst | currency:'AUD':'symbol':'1.2-2' }}</span>
            </div>
            <div class="total-line gst-line">
              <span>GST (10%)</span>
              <span>{{ receipt()!.gstTotal | currency:'AUD':'symbol':'1.2-2' }}</span>
            </div>
            <div class="receipt-rule thin"></div>
            <div class="total-line grand-total-line">
              <span>TOTAL</span>
              <span>{{ receipt()!.grandTotal | currency:'AUD':'symbol':'1.2-2' }}</span>
            </div>
            @if (receipt()!.tipTotal > 0) {
              <div class="total-line tip-line">
                <span>Tip</span>
                <span>{{ receipt()!.tipTotal | currency:'AUD':'symbol':'1.2-2' }}</span>
              </div>
            }

            <div class="receipt-rule"></div>

            @for (p of receipt()!.payments; track $index) {
              <div class="payment-line" [class.tip-payment-line]="p.isTip">
                <span class="payment-label">
                  {{ p.method }}
                  @if (p.reference) { {{ p.reference }} }
                  @if (p.isTip) { <span class="tip-badge">TIP</span> }
                </span>
                <span>{{ p.amount | currency:'AUD':'symbol':'1.2-2' }}</span>
              </div>
            }

            <div class="receipt-rule"></div>

            <div class="receipt-footer">
              <div class="paid-stamp">✓ PAID</div>
              <div class="gst-note">* All prices include GST where applicable</div>
              <div class="gst-note">Tax Invoice issued under Australian Tax Law</div>
            </div>

          </div>

          <div class="receipt-actions">
            <button mat-raised-button color="primary" (click)="newTab()">
              <mat-icon>add</mat-icon>
              New Tab
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .receipt-page {
      min-height: 100vh;
      background: #f0f0f0;
      display: flex;
      justify-content: center;
      padding: 32px 16px 64px;
    }

    .loading-state, .error-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 16px;
      padding: 64px;
      color: #666;
      mat-icon { font-size: 48px; height: 48px; width: 48px; color: #e53935; }
      p { margin: 0; }
    }

    .receipt-wrapper {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 24px;
      width: 100%;
      max-width: 480px;
    }

    .receipt {
      background: white;
      width: 100%;
      padding: 28px 28px 24px;
      box-shadow: 0 4px 16px rgba(0,0,0,0.12);
      border-radius: 4px;
      font-family: 'Courier New', Courier, monospace;
      font-size: 13px;
    }

    .receipt-header {
      text-align: center;
      margin-bottom: 16px;
    }

    .receipt-title {
      font-size: 20px;
      font-weight: 700;
      letter-spacing: 3px;
      margin-bottom: 8px;
    }

    .venue-name {
      font-size: 15px;
      font-weight: 700;
      margin-bottom: 4px;
    }

    .receipt-meta {
      font-size: 12px;
      color: #555;
      line-height: 1.6;
    }

    .receipt-number {
      font-weight: 600;
      color: #333;
    }

    .receipt-rule {
      border: none;
      border-top: 2px solid #333;
      margin: 10px 0;
    }

    .receipt-rule.thin {
      border-top: 1px dashed #bbb;
    }

    .items-header {
      display: grid;
      grid-template-columns: 1fr 36px 80px 80px;
      gap: 4px;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1px;
      padding: 4px 0;
    }

    .col-qty, .col-unit, .col-total {
      text-align: right;
    }

    .item-line {
      display: grid;
      grid-template-columns: 1fr 36px 80px 80px;
      gap: 4px;
      padding: 4px 0;
      border-bottom: 1px solid #f5f5f5;
      font-size: 13px;
      &:last-child { border-bottom: none; }
    }

    .item-name-col {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .no-items {
      padding: 8px 0;
      color: #bbb;
      font-style: italic;
      font-size: 12px;
    }

    .total-line {
      display: flex;
      justify-content: space-between;
      padding: 3px 0;
      font-size: 13px;
    }

    .gst-line {
      color: #555;
    }

    .grand-total-line {
      font-weight: 700;
      font-size: 16px;
      padding: 4px 0;
    }

    .tip-line {
      color: #f9a825;
      font-weight: 600;
    }

    .payment-line {
      display: flex;
      justify-content: space-between;
      padding: 4px 0;
      font-size: 13px;
    }

    .payment-label {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .tip-payment-line {
      color: #f9a825;
    }

    .tip-badge {
      font-size: 10px;
      background: #f9a825;
      color: white;
      padding: 1px 4px;
      border-radius: 2px;
      font-weight: 700;
    }

    .receipt-footer {
      text-align: center;
      margin-top: 8px;
    }

    .paid-stamp {
      font-size: 18px;
      font-weight: 700;
      letter-spacing: 4px;
      color: #388e3c;
      margin-bottom: 8px;
    }

    .gst-note {
      font-size: 10px;
      color: #888;
      line-height: 1.5;
    }

    .receipt-actions {
      display: flex;
      gap: 12px;
    }
  `]
})
export class ReceiptComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);

  readonly receipt = signal<ReceiptData | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const tabId = this.route.snapshot.paramMap.get('tabId');
    if (!tabId) {
      this.router.navigate(['/catalogue']);
      return;
    }

    this.api.get<ReceiptData>(`/api/tabs/${tabId}/receipt`).subscribe({
      next: (data) => {
        this.receipt.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load receipt. The tab may not exist.');
        this.loading.set(false);
      }
    });
  }

  newTab(): void {
    this.router.navigate(['/catalogue']);
  }
}
