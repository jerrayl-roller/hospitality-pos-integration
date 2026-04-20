import { Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

export interface GuestConfirmData {
  guestName: string;
  guestEmail: string;
  guestPhone: string;
}

@Component({
  selector: 'app-guest-confirm-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Confirm Guest Details</h2>
    <mat-dialog-content>
      <p class="intro">Review and edit guest details before creating the tab.</p>
      <form [formGroup]="form" class="guest-form">
        <mat-form-field appearance="outline">
          <mat-label>Guest Name</mat-label>
          <input matInput formControlName="guestName" placeholder="e.g. Jane Smith" />
          <mat-icon matSuffix>person</mat-icon>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput formControlName="guestEmail" type="email" placeholder="jane@example.com" />
          <mat-icon matSuffix>email</mat-icon>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Phone</mat-label>
          <input matInput formControlName="guestPhone" type="tel" placeholder="+61 400 000 000" />
          <mat-icon matSuffix>phone</mat-icon>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(null)">Cancel</button>
      <button mat-raised-button color="primary" (click)="confirm()">Import Booking</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .intro { margin: 0 0 12px; font-size: 13px; color: #666; }
    .guest-form { display: flex; flex-direction: column; gap: 4px; min-width: 340px; }
    mat-form-field { width: 100%; }
  `]
})
export class GuestConfirmDialogComponent {
  readonly ref = inject(MatDialogRef<GuestConfirmDialogComponent>);
  private readonly data = inject<GuestConfirmData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  form = this.fb.group({
    guestName: [this.data.guestName],
    guestEmail: [this.data.guestEmail],
    guestPhone: [this.data.guestPhone]
  });

  confirm(): void {
    this.ref.close({
      guestName: this.form.value.guestName ?? '',
      guestEmail: this.form.value.guestEmail ?? '',
      guestPhone: this.form.value.guestPhone ?? ''
    } satisfies GuestConfirmData);
  }
}
