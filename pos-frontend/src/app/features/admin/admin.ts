import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { TabStateService } from '../../core/tab-state.service';
import { NotificationService } from '../../core/notification.service';
import { ConfirmDialogComponent } from './confirm-dialog';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatCardModule, MatProgressSpinnerModule, MatDialogModule],
  templateUrl: './admin.html',
  styleUrl: './admin.scss'
})
export class AdminComponent {
  private readonly api = inject(ApiService);
  private readonly tabState = inject(TabStateService);
  private readonly notifications = inject(NotificationService);
  private readonly dialog = inject(MatDialog);

  resyncing = signal(false);
  clearing = signal(false);
  releasingLocks = signal(false);
  resyncResult = signal<string | null>(null);
  clearResult = signal<string | null>(null);
  lockReleaseResult = signal<{ released: string[]; failed: { bookingUniqueId: string; error: string }[] } | null>(null);

  confirmResync(): void {
    this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Force Product Resync',
        message: 'This will clear the product cache. The next catalogue load will pull fresh data from ROLLER. Continue?',
        confirmLabel: 'Resync Products',
        confirmColor: 'primary'
      },
      width: '420px'
    }).afterClosed().subscribe(confirmed => {
      if (confirmed) this.runResync();
    });
  }

  confirmReleaseLocks(): void {
    this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Force Release All Locks',
        message: 'This will call the ROLLER payment-lock release endpoint for every booking-linked tab in the system. Use this to clean up locks after a reset or crash. Continue?',
        confirmLabel: 'Release All Locks',
        confirmColor: 'warn'
      },
      width: '420px'
    }).afterClosed().subscribe(confirmed => {
      if (confirmed) this.runReleaseLocks();
    });
  }

  confirmClear(): void {
    this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete All Tabs & Payments',
        message: 'This will permanently delete all tabs and payment records. This cannot be undone. Continue?',
        confirmLabel: 'Delete All',
        confirmColor: 'warn'
      },
      width: '420px'
    }).afterClosed().subscribe(confirmed => {
      if (confirmed) this.runClear();
    });
  }

  private runResync(): void {
    this.resyncing.set(true);
    this.resyncResult.set(null);
    this.api.post<{ message: string }>('/api/admin/resync-products', {}).subscribe({
      next: res => {
        this.resyncing.set(false);
        this.resyncResult.set(res.message);
        this.notifications.info('Product cache cleared. Will resync on next catalogue load.');
      },
      error: () => this.resyncing.set(false)
    });
  }

  private runReleaseLocks(): void {
    this.releasingLocks.set(true);
    this.lockReleaseResult.set(null);
    this.api.delete<{ released: string[]; failed: { bookingUniqueId: string; error: string }[] }>('/api/admin/tabs/locks').subscribe({
      next: res => {
        this.releasingLocks.set(false);
        this.lockReleaseResult.set(res);
        const msg = res.failed.length === 0
          ? `Released ${res.released.length} lock(s) successfully.`
          : `Released ${res.released.length} lock(s). ${res.failed.length} failed — see details below.`;
        res.failed.length > 0 ? this.notifications.error(msg) : this.notifications.info(msg);
      },
      error: () => this.releasingLocks.set(false)
    });
  }

  private runClear(): void {
    this.clearing.set(true);
    this.clearResult.set(null);
    this.tabState.clearTab();
    this.api.post<{ message: string }>('/api/admin/clear-data', {}).subscribe({
      next: res => {
        this.clearing.set(false);
        this.clearResult.set(res.message);
        this.notifications.info('All tabs and payments deleted.');
      },
      error: () => this.clearing.set(false)
    });
  }
}
