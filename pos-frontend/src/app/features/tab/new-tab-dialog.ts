import { Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

export interface NewTabResult {
  guestName: string;
  guestEmail: string;
  guestPhone: string;
}

@Component({
  selector: 'app-new-tab-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Open New Tab</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="tab-form">
        <mat-form-field appearance="outline">
          <mat-label>Guest Name</mat-label>
          <input matInput formControlName="guestName" placeholder="e.g. Jane Smith" />
          <mat-icon matSuffix>person</mat-icon>
          <mat-error *ngIf="form.get('guestName')?.hasError('required')">Name is required</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Email (optional)</mat-label>
          <input matInput formControlName="guestEmail" type="email" placeholder="jane@example.com" />
          <mat-icon matSuffix>email</mat-icon>
          <mat-error *ngIf="form.get('guestEmail')?.hasError('email')">Enter a valid email</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Phone (optional)</mat-label>
          <input matInput formControlName="guestPhone" type="tel" placeholder="+61 400 000 000" />
          <mat-icon matSuffix>phone</mat-icon>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(null)">Cancel</button>
      <button mat-raised-button color="primary" [disabled]="form.invalid" (click)="submit()">
        Open Tab
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .tab-form { display: flex; flex-direction: column; gap: 4px; padding-top: 8px; min-width: 320px; }
    mat-form-field { width: 100%; }
  `]
})
export class NewTabDialogComponent {
  readonly ref = inject(MatDialogRef<NewTabDialogComponent>);
  private readonly fb = inject(FormBuilder);

  form = this.fb.group({
    guestName: ['', Validators.required],
    guestEmail: ['', Validators.email],
    guestPhone: ['']
  });

  submit(): void {
    if (this.form.valid) {
      this.ref.close({
        guestName: this.form.value.guestName ?? '',
        guestEmail: this.form.value.guestEmail ?? '',
        guestPhone: this.form.value.guestPhone ?? ''
      } satisfies NewTabResult);
    }
  }
}
