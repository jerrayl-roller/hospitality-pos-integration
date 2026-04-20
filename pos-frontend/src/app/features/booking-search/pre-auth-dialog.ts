import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface PreAuthDialogData {
  cardNumber: string | null;
  cardType: string | null;
}

@Component({
  selector: 'app-pre-auth-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Simulated Pre-Authorisation</h2>
    <mat-dialog-content>
      <p class="intro">Tab opened successfully.</p>
      <p class="label">Pre-Auth Card:</p>
      <div class="card-display">
        <span class="card-logo" [class]="'card-logo--' + (data.cardType ?? '')">{{ cardLabel }}</span>
        <span class="card-number">•••• •••• •••• {{ last4 }}</span>
      </div>
      <p class="disclaimer">This is a simulated card for prototype purposes only.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-raised-button color="primary" (click)="ref.close()">OK</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { display: flex; flex-direction: column; gap: 8px; padding-top: 8px; min-width: 360px; }
    .intro { margin: 0; font-size: 14px; }
    .label { margin: 8px 0 4px; font-size: 13px; color: #666; font-weight: 500; }
    .card-display {
      display: flex; align-items: center; gap: 14px;
      padding: 14px 18px;
      background: #f5f5f5;
      border: 1px solid #ddd;
      border-radius: 6px;
    }
    .card-number {
      font-family: monospace; font-size: 18px; font-weight: 700;
      letter-spacing: 2px; color: #1a237e;
    }
    .card-logo {
      display: inline-block; padding: 4px 10px; border-radius: 4px;
      font-size: 13px; font-weight: 800; letter-spacing: 1px; color: #fff; white-space: nowrap;
    }
    .card-logo--visa { background: #1a1f71; font-style: italic; }
    .card-logo--mastercard { background: #eb001b; }
    .card-logo--amex { background: #007b5e; }
    .disclaimer { margin: 4px 0 0; font-size: 12px; color: #999; }
  `]
})
export class PreAuthDialogComponent {
  readonly ref = inject(MatDialogRef<PreAuthDialogComponent>);
  readonly data = inject<PreAuthDialogData>(MAT_DIALOG_DATA);

  get last4(): string {
    return this.data.cardNumber?.split('-').pop() ?? '????';
  }

  get cardLabel(): string {
    switch (this.data.cardType) {
      case 'visa': return 'VISA';
      case 'mastercard': return 'MC';
      case 'amex': return 'AMEX';
      default: return 'CARD';
    }
  }
}
