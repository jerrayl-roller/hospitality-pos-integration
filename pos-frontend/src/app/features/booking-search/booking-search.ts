import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { Subscription } from 'rxjs';
import { of } from 'rxjs';
import { debounceTime, distinctUntilChanged, catchError } from 'rxjs/operators';
import { ApiService } from '../../core/api.service';
import { TabStateService, Tab } from '../../core/tab-state.service';
import { NotificationService } from '../../core/notification.service';
import { PreAuthDialogComponent } from './pre-auth-dialog';
import { GuestConfirmDialogComponent, GuestConfirmData } from './guest-confirm-dialog';

export interface BookingItemPreview {
  productName: string;
  quantity: number;
}

export interface GuestDetails {
  firstName: string | null;
  lastName: string | null;
  email: string | null;
  phone: string | null;
}

export interface BookingSummary {
  bookingUniqueId: string;
  bookingReference: string | null;
  guestName: string | null;
  bookingDate: string | null;
  status: string | null;
  totalAmount: number;
  lineItemCount: number;
  items: BookingItemPreview[];
  customerId: number | null;
  isImported: boolean;
}

interface ErrorBody {
  error: string;
  existingTabId?: string;
  detail?: string;
}

@Component({
  selector: 'app-booking-search',
  standalone: true,
  imports: [
    CommonModule,
    CurrencyPipe,
    DatePipe,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatExpansionModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './booking-search.html',
  styleUrl: './booking-search.scss'
})
export class BookingSearchComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly tabState = inject(TabStateService);
  private readonly notification = inject(NotificationService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  readonly searchControl = new FormControl('');
  results: BookingSummary[] = [];
  loading = false;
  searched = false;
  importingId: string | null = null;

  private sub!: Subscription;

  ngOnInit(): void {
    this.sub = this.searchControl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged()
    ).subscribe(q => {
      if (!q || q.length < 3) {
        this.results = [];
        this.searched = false;
        return;
      }
      this.doSearch(q);
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  get queryTooShort(): boolean {
    const v = this.searchControl.value ?? '';
    return v.length > 0 && v.length < 3;
  }

  clearSearch(): void {
    this.searchControl.setValue('');
    this.results = [];
    this.searched = false;
  }

  private doSearch(q: string): void {
    this.loading = true;
    this.searched = true;
    this.api.get<BookingSummary[]>(`/api/bookings/search?q=${encodeURIComponent(q)}`).subscribe({
      next: res => { this.results = res; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  importBooking(booking: BookingSummary): void {
    this.importingId = booking.bookingUniqueId;

    const guest$ = booking.customerId
      ? this.api.get<GuestDetails>(`/api/guests/${booking.customerId}`).pipe(catchError(() => of(null)))
      : of(null);

    guest$.subscribe({
      next: guest => {
      const guestName = [guest?.firstName, guest?.lastName].filter(Boolean).join(' ');

      this.dialog.open(GuestConfirmDialogComponent, {
        width: '400px',
        data: { guestName, guestEmail: guest?.email ?? '', guestPhone: guest?.phone ?? '' } satisfies GuestConfirmData
      }).afterClosed().subscribe((result: GuestConfirmData | null | undefined) => {
        if (result == null) {
          this.importingId = null;
          return;
        }

        this.api.post<Tab>('/api/tabs/from-booking', {
          bookingUniqueId: booking.bookingUniqueId,
          guestName: result.guestName || null,
          guestEmail: result.guestEmail || null,
          guestPhone: result.guestPhone || null
        }).subscribe({
          next: tab => {
            this.importingId = null;
            this.tabState.refreshTab(tab.tabId).subscribe(() => {
              this.dialog.open(PreAuthDialogComponent, {
                data: { cardNumber: tab.preAuthCardNumber, cardType: tab.preAuthCardType },
                width: '440px',
                disableClose: true
              }).afterClosed().subscribe(() => {
                this.router.navigate(['/catalogue']);
              });
            });
          },
          error: (err: HttpErrorResponse) => {
            this.importingId = null;
            const body = err.error as ErrorBody;
            if (err.status === 409 && body?.error === 'tab_already_open' && body.existingTabId) {
              this.notification.info('A tab is already open for this booking.');
              this.tabState.refreshTab(body.existingTabId).subscribe(() => {
                this.router.navigate(['/catalogue']);
              });
            } else if (err.status === 409 && body?.error === 'booking_already_imported') {
              this.notification.error('This booking has already been imported.');
            } else if (err.status === 409 && body?.error === 'booking_fully_prepaid') {
              this.notification.info('This booking has been fully paid. No tab required.');
            } else if (err.status === 503 && body?.error === 'payment_lock_failed') {
              this.notification.error('Could not lock this booking. Failed to contact external system. Please try again or contact support.');
            } else {
              this.notification.error('Failed to import booking. Please try again.');
            }
          }
        });
      });
      },
      error: () => { this.importingId = null; }
    });
  }

  statusClass(status: string | null): string {
    switch (status) {
      case 'PaidFull': case 'Paid': return 'status-paid';
      case 'Cancelled': case 'Deleted': return 'status-cancelled';
      case 'PaidPart': case 'PartiallyPaid': return 'status-partial';
      default: return 'status-other';
    }
  }

  statusLabel(status: string | null): string {
    switch (status) {
      case 'PaidFull': return 'Paid';
      case 'PaidPart': return 'Part Paid';
      case 'PartiallyPaid': return 'Part Paid';
      case 'PendingPayment': return 'Pending';
      case 'NoPaymentRequired': return 'No Payment';
      case 'Cancelled': return 'Cancelled';
      case 'Deleted': return 'Deleted';
      default: return status ?? 'Unknown';
    }
  }
}
