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

interface BookingResyncOutcome {
  tabId: string;
  bookingUniqueId: string | null;
  status: 'updated' | 'unchanged' | 'errored' | 'failed';
  detail: string | null;
}

interface BookingResyncResult {
  processed: number;
  updated: number;
  errored: number;
  failed: number;
  outcomes: BookingResyncOutcome[];
}

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
  resyncingBookings = signal(false);
  resyncResult = signal<string | null>(null);
  clearResult = signal<string | null>(null);
  lockReleaseResult = signal<{ released: string[]; failed: { bookingUniqueId: string; error: string }[] } | null>(null);
  bookingResyncResult = signal<BookingResyncResult | null>(null);

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

  confirmResyncBookings(): void {
    this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Force Resync All Bookings',
        message: 'This pulls the latest state from ROLLER for every booking-linked tab and refreshes imported items and totals. Tabs where ROLLER reports additional payments since import will be flagged as errored. Continue?',
        confirmLabel: 'Resync Bookings',
        confirmColor: 'primary'
      },
      width: '460px'
    }).afterClosed().subscribe(confirmed => {
      if (confirmed) this.runResyncBookings();
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

  private runResyncBookings(): void {
    this.resyncingBookings.set(true);
    this.bookingResyncResult.set(null);
    this.api.post<BookingResyncResult>('/api/admin/resync-bookings', {}).subscribe({
      next: res => {
        this.resyncingBookings.set(false);
        this.bookingResyncResult.set(res);
        const parts = [`${res.updated} updated`, `${res.errored} errored`, `${res.failed} failed`];
        const msg = `Resynced ${res.processed} tab(s): ${parts.join(', ')}.`;
        (res.errored > 0 || res.failed > 0) ? this.notifications.error(msg) : this.notifications.info(msg);

        // TabStateService holds the active tab in memory; pull fresh state so the right-hand
        // panel reflects any booking items / totals that changed during the resync.
        const active = this.tabState.currentTab;
        if (active) {
          const outcome = res.outcomes.find(o => o.tabId === active.tabId);
          if (!outcome || outcome.status === 'updated' || outcome.status === 'errored') {
            this.tabState.refreshTab(active.tabId).subscribe();
          }
        }
      },
      error: () => this.resyncingBookings.set(false)
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
