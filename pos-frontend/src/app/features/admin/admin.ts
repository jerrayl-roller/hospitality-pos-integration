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

  resetting = signal(false);
  lastResult = signal<string | null>(null);

  confirmReset(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Clear Database & Resync',
        message: 'This will permanently delete all tabs and payments, and clear the product cache. The next catalogue load will pull fresh data from ROLLER. Continue?',
        confirmLabel: 'Clear & Resync',
        confirmColor: 'warn'
      },
      width: '420px'
    });

    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) this.runReset();
    });
  }

  private runReset(): void {
    this.resetting.set(true);
    this.lastResult.set(null);
    this.tabState.clearTab();

    this.api.post<{ message: string }>('/api/admin/reset', {}).subscribe({
      next: res => {
        this.resetting.set(false);
        this.lastResult.set(res.message);
        this.notifications.info('Database cleared. Products will resync on next catalogue load.');
      },
      error: () => {
        this.resetting.set(false);
        this.lastResult.set(null);
      }
    });
  }
}
