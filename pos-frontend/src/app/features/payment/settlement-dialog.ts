import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { CommonModule, CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { ApiService } from '../../core/api.service';
import { TabStateService, Tab } from '../../core/tab-state.service';
import { NotificationService } from '../../core/notification.service';

@Component({
  selector: 'app-settlement-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    CurrencyPipe,
    MatDialogModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatDividerModule,
    MatProgressSpinnerModule,
    MatExpansionModule
  ],
  template: `
    <h2 mat-dialog-title class="dialog-title">
      <mat-icon>point_of_sale</mat-icon>
      Settle Tab
    </h2>

    <mat-dialog-content class="dialog-content">

      <!-- Items summary -->
      <div class="section-label">Items</div>
      @if (tab().importedItems && tab().importedItems.length > 0) {
        <mat-expansion-panel class="items-panel">
          <mat-expansion-panel-header>
            <mat-panel-title>Booking Items</mat-panel-title>
            <mat-panel-description>{{ tab().importedItems.length }} item{{ tab().importedItems.length === 1 ? '' : 's' }}</mat-panel-description>
          </mat-expansion-panel-header>
          @for (item of tab().importedItems; track item.productId) {
            <div class="item-row">
              <span class="item-name">{{ item.productName }}</span>
              <span class="item-detail">x{{ item.quantity }}</span>
              <span class="item-amount">{{ item.unitPrice * item.quantity | currency:'AUD':'symbol':'1.2-2' }}</span>
            </div>
          }
        </mat-expansion-panel>
      }
      @for (item of tab().addedItems; track item.productId) {
        <div class="item-row item-row--added">
          <span class="item-name">{{ item.productName }}</span>
          <span class="item-detail">x{{ item.quantity }}</span>
          <span class="item-amount">{{ item.unitPrice * item.quantity | currency:'AUD':'symbol':'1.2-2' }}</span>
        </div>
      }
      @if (tab().addedItems.length === 0 && (!tab().importedItems || tab().importedItems.length === 0)) {
        <div class="empty-items">No items on tab</div>
      }

      <div class="grand-total-row">
        <span>Grand Total</span>
        <span class="grand-total-amount">{{ tab().grandTotal | currency:'AUD':'symbol':'1.2-2' }}</span>
      </div>

      <!-- Payments made -->
      @if (tab().payments && tab().payments.length > 0) {
        <mat-divider></mat-divider>
        <div class="section-label">Payments Applied</div>
        @for (p of tab().payments; track p.paymentId) {
          <div class="payment-row" [class.tip-row]="p.isTip">
            <div class="payment-method">
              <mat-icon class="method-icon">{{ methodIcon(p.method) }}</mat-icon>
              <span>{{ methodLabel(p.method) }}{{ p.cardNumberMasked ? ' ****' + p.cardNumberMasked.slice(-4) : '' }}</span>
              @if (p.isTip) { <span class="tip-label">TIP</span> }
            </div>
            <span class="payment-amount">{{ p.amount | currency:'AUD':'symbol':'1.2-2' }}</span>
          </div>
        }
      }

      <mat-divider></mat-divider>

      <!-- Amount remaining -->
      <div class="remaining-row" [class.fully-paid]="!hasMoreToPay()">
        <span class="remaining-label">{{ hasMoreToPay() ? 'Amount Remaining' : 'Fully Paid' }}</span>
        <span class="remaining-amount">
          @if (hasMoreToPay()) {
            {{ tab().amountRemaining | currency:'AUD':'symbol':'1.2-2' }}
          } @else {
            <mat-icon class="paid-icon">check_circle</mat-icon>
          }
        </span>
      </div>

      <!-- Add payment form -->
      @if (hasMoreToPay()) {
        <mat-divider></mat-divider>
        <div class="section-label">Add Payment</div>

        <mat-button-toggle-group
          [ngModel]="selectedMethod()"
          (ngModelChange)="onMethodChange($event)"
          class="method-toggle">
          @if (tab().preAuthCardNumber) {
            <mat-button-toggle value="pre_auth_card">
              <mat-icon>credit_card</mat-icon>
              Pre-Auth (****{{ preAuthLast4() }})
            </mat-button-toggle>
          }
          <mat-button-toggle value="new_card">
            <mat-icon>add_card</mat-icon>
            New Card
          </mat-button-toggle>
          <mat-button-toggle value="cash">
            <mat-icon>payments</mat-icon>
            Cash
          </mat-button-toggle>
          <mat-button-toggle value="gift_card">
            <mat-icon>card_giftcard</mat-icon>
            Gift Card
          </mat-button-toggle>
        </mat-button-toggle-group>

        @if (selectedMethod() === 'gift_card') {
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Gift Card Number</mat-label>
            <mat-icon matPrefix>card_giftcard</mat-icon>
            <input matInput
              [ngModel]="giftCardNumber()"
              (ngModelChange)="giftCardNumber.set($event)"
              placeholder="Enter card number"
              autocomplete="off">
          </mat-form-field>
        }

        <div class="payment-inputs">
          <mat-form-field appearance="outline" class="amount-field">
            <mat-label>Amount</mat-label>
            <span matTextPrefix>$&nbsp;</span>
            <input matInput type="number"
              [ngModel]="paymentAmount()"
              (ngModelChange)="paymentAmount.set(+$event || 0)"
              min="0.01"
              [max]="tab().amountRemaining"
              step="0.01">
          </mat-form-field>
          @if (selectedMethod() !== 'gift_card') {
            <mat-form-field appearance="outline" class="tip-field">
              <mat-label>Tip (optional)</mat-label>
              <span matTextPrefix>$&nbsp;</span>
              <input matInput type="number"
                [ngModel]="tipAmount()"
                (ngModelChange)="tipAmount.set(+$event || 0)"
                min="0"
                step="0.01">
            </mat-form-field>
          }
        </div>

        <button mat-raised-button color="accent" class="add-btn"
          [disabled]="adding() || paymentAmount() <= 0"
          (click)="addPayment()">
          @if (adding()) {
            <mat-spinner diameter="20" class="btn-spinner"></mat-spinner>
          } @else {
            <mat-icon>add</mat-icon>
            Add Payment
          }
        </button>
      }

    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="cancel()" [disabled]="settling()">Cancel</button>
      <button mat-raised-button color="primary"
        [disabled]="!canSettle() || settling()"
        (click)="settle()">
        @if (settling()) {
          <mat-spinner diameter="20" class="btn-spinner"></mat-spinner>
        } @else {
          <mat-icon>receipt_long</mat-icon>
          Settle &amp; Print Receipt
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-title {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 18px;
    }
    .dialog-content {
      min-width: 520px;
      max-height: 70vh;
      padding: 0 24px 8px;
    }
    .section-label {
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1px;
      text-transform: uppercase;
      color: #999;
      padding: 12px 0 6px;
    }
    .items-panel {
      box-shadow: none !important;
      border: 1px solid #e0e0e0;
      border-radius: 4px !important;
      margin-bottom: 4px;
    }
    .item-row {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 6px 0;
      border-bottom: 1px solid #f5f5f5;
      &:last-child { border-bottom: none; }
    }
    .item-row--added {
      padding: 6px 16px;
    }
    .item-name {
      flex: 1;
      font-size: 13px;
    }
    .item-detail {
      font-size: 12px;
      color: #999;
      min-width: 28px;
      text-align: center;
    }
    .item-amount {
      font-size: 13px;
      font-weight: 600;
      min-width: 80px;
      text-align: right;
    }
    .empty-items {
      padding: 8px 0;
      font-size: 13px;
      color: #bbb;
      font-style: italic;
    }
    .grand-total-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 0 6px;
      font-size: 13px;
      color: #555;
    }
    .grand-total-amount {
      font-size: 15px;
      font-weight: 600;
    }
    .payment-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 6px 0;
      border-bottom: 1px solid #f5f5f5;
    }
    .tip-row {
      background: #fffde7;
      border-radius: 4px;
      padding: 4px 8px;
      margin: 2px 0;
    }
    .payment-method {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
    }
    .method-icon {
      font-size: 18px;
      height: 18px;
      width: 18px;
      color: #666;
    }
    .tip-label {
      font-size: 10px;
      font-weight: 700;
      background: #f9a825;
      color: white;
      padding: 1px 5px;
      border-radius: 3px;
    }
    .payment-amount {
      font-size: 13px;
      font-weight: 600;
      color: #388e3c;
    }
    .remaining-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 0 8px;
      font-weight: 600;
    }
    .remaining-label {
      font-size: 14px;
    }
    .remaining-amount {
      font-size: 22px;
      font-weight: 700;
      color: #d32f2f;
    }
    .remaining-row.fully-paid .remaining-label {
      color: #388e3c;
    }
    .paid-icon {
      color: #388e3c;
      font-size: 28px;
      height: 28px;
      width: 28px;
    }
    .method-toggle {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
      margin-bottom: 12px;
      ::ng-deep .mat-button-toggle {
        font-size: 12px;
      }
    }
    .full-width {
      width: 100%;
      margin-bottom: 8px;
    }
    .payment-inputs {
      display: flex;
      gap: 12px;
      margin-bottom: 12px;
    }
    .amount-field {
      flex: 2;
    }
    .tip-field {
      flex: 1;
    }
    .add-btn {
      width: 100%;
      margin-bottom: 4px;
    }
    .btn-spinner {
      display: inline-block;
    }
  `]
})
export class SettlementDialogComponent implements OnInit {
  private readonly dialogRef = inject(MatDialogRef<SettlementDialogComponent>);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  readonly tabState = inject(TabStateService);
  private readonly notify = inject(NotificationService);

  readonly tab = signal<Tab>(inject<Tab>(MAT_DIALOG_DATA));

  readonly selectedMethod = signal('pre_auth_card');
  readonly paymentAmount = signal(0);
  readonly tipAmount = signal(0);
  readonly giftCardNumber = signal('');
  readonly adding = signal(false);
  readonly settling = signal(false);

  readonly canSettle = computed(() => this.tab().amountRemaining <= 0.005);
  readonly hasMoreToPay = computed(() => this.tab().amountRemaining > 0.005);
  readonly preAuthLast4 = computed(() => this.tab().preAuthCardNumber?.split('-').pop() ?? '');

  ngOnInit(): void {
    this.paymentAmount.set(Math.max(0, Math.round(this.tab().amountRemaining * 100) / 100));
    if (!this.tab().preAuthCardNumber) {
      this.selectedMethod.set('new_card');
    }
  }

  methodLabel(method: string): string {
    const labels: Record<string, string> = {
      visa: 'Visa', mastercard: 'Mastercard', amex: 'Amex',
      cash: 'Cash', gift_card: 'Gift Card', card: 'Card',
      booking_payment: 'Pre-paid'
    };
    return labels[method] ?? method;
  }

  methodIcon(method: string): string {
    if (method === 'cash') return 'payments';
    if (method === 'gift_card') return 'card_giftcard';
    return 'credit_card';
  }

  onMethodChange(method: string): void {
    this.selectedMethod.set(method);
    if (method === 'gift_card') {
      this.tipAmount.set(0);
    }
  }

  addPayment(): void {
    if (this.adding()) return;
    if (this.paymentAmount() <= 0) {
      this.notify.error('Payment amount must be greater than zero');
      return;
    }
    if (this.selectedMethod() === 'gift_card' && !this.giftCardNumber().trim()) {
      this.notify.error('Please enter a gift card number');
      return;
    }

    this.adding.set(true);
    const req = {
      method: this.selectedMethod(),
      amount: this.paymentAmount(),
      tipAmount: this.tipAmount(),
      giftCardNumber: this.selectedMethod() === 'gift_card' ? this.giftCardNumber().trim() : undefined
    };

    this.api.post<Tab>(`/api/tabs/${this.tab().tabId}/payments`, req).subscribe({
      next: (updated) => {
        this.tab.set(updated);
        this.tabState.applyTabUpdate(updated);
        this.paymentAmount.set(Math.max(0, Math.round(updated.amountRemaining * 100) / 100));
        this.tipAmount.set(0);
        this.giftCardNumber.set('');
        this.adding.set(false);
      },
      error: (err) => {
        const code = err?.error?.error;
        const detail = err?.error?.detail;
        if (code === 'exceeds_outstanding') {
          this.notify.error('Amount exceeds the outstanding balance');
        } else if (code === 'gift_card_number_required') {
          this.notify.error('Please enter a gift card number');
        } else if (code === 'gift_card_not_found') {
          this.notify.error('Gift card not found');
        } else if (code === 'gift_card_expired') {
          this.notify.error('Gift card has expired');
        } else if (code === 'gift_card_inactive') {
          this.notify.error('Gift card is not active');
        } else if (code === 'gift_card_insufficient_balance') {
          this.notify.error(detail
            ? `Insufficient gift card balance — $${detail} available`
            : 'Insufficient gift card balance');
        } else if (code === 'gift_card_deduct_failed' || code === 'gift_card_check_failed') {
          this.notify.error('Gift card payment failed — please try again or use a different payment method');
        } else {
          this.notify.error('Failed to add payment');
        }
        this.adding.set(false);
      }
    });
  }

  settle(): void {
    if (!this.canSettle() || this.settling()) return;
    this.settling.set(true);

    this.api.post<Tab>(`/api/tabs/${this.tab().tabId}/settle`, {}).subscribe({
      next: () => {
        const tabId = this.tab().tabId;
        this.tabState.clearTab();
        this.dialogRef.close();
        this.router.navigate(['/tabs'], { queryParams: { receipt: tabId } });
      },
      error: (err) => {
        const code = err?.error?.error;
        if (code === 'not_fully_paid') {
          this.notify.error('Tab has an outstanding balance — add a payment first');
        } else {
          this.notify.error('Failed to settle tab');
        }
        this.settling.set(false);
      }
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
