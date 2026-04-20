import { Injectable, inject } from '@angular/core';
import { BehaviorSubject, Observable, of, tap } from 'rxjs';
import { ApiService } from './api.service';

export interface TabLineItem {
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
}

export interface TabPayment {
  paymentId: string;
  type: string;
  method: string;
  cardNumberMasked: string | null;
  amount: number;
  isTip: boolean;
  status: string;
  createdAt: string;
}

export interface Tab {
  tabId: string;
  bookingUniqueId: string | null;
  bookingReference: string | null;
  guestName: string | null;
  guestEmail: string | null;
  guestPhone: string | null;
  importedItems: TabLineItem[];
  addedItems: TabLineItem[];
  grandTotal: number;
  amountRemaining: number;
  payments: TabPayment[];
  paymentStatus: string;
  preAuthStatus: string;
  preAuthCardNumber: string | null;
  preAuthCardType: string | null;
  hasPendingConflict: boolean;
  openedAt: string;
  settledAt: string | null;
}

export interface CreateTabRequest {
  guestName: string;
  guestEmail: string;
  guestPhone: string;
}

export interface AddPaymentRequest {
  method: string;
  amount: number;
  tipAmount: number;
  giftCardNumber?: string;
}

const SESSION_KEY = 'pos_active_tab';

@Injectable({ providedIn: 'root' })
export class TabStateService {
  private readonly api = inject(ApiService);
  private readonly _tab$ = new BehaviorSubject<Tab | null>(this.loadFromSession());

  readonly tab$ = this._tab$.asObservable();

  private _isExistingTab = false;
  private _originalSnapshot: TabLineItem[] | null = null;

  get currentTab(): Tab | null {
    return this._tab$.value;
  }

  get isExistingTab(): boolean {
    return this._isExistingTab;
  }

  get hasChanges(): boolean {
    const tab = this._tab$.value;
    if (!tab || this._originalSnapshot === null) return false;
    return JSON.stringify(tab.addedItems) !== JSON.stringify(this._originalSnapshot);
  }

  openNewTab(req: CreateTabRequest) {
    return this.api.post<Tab>('/api/tabs', req).pipe(
      tap(tab => {
        this._isExistingTab = false;
        this._originalSnapshot = [];
        this.setTab(tab);
      })
    );
  }

  refreshTab(tabId: string) {
    return this.api.get<Tab>(`/api/tabs/${tabId}`).pipe(
      tap(tab => {
        this._isExistingTab = true;
        this._originalSnapshot = tab.addedItems.map(i => ({ ...i }));
        this.setTab(tab);
      })
    );
  }

  addItem(tabId: string, item: { productId: string; productName: string; quantity: number; unitPrice: number }) {
    return this.api.post<Tab>(`/api/tabs/${tabId}/items`, item).pipe(
      tap(tab => this.setTab(tab))
    );
  }

  removeItem(tabId: string, productId: string) {
    return this.api.delete<Tab>(`/api/tabs/${tabId}/items/${productId}`).pipe(
      tap(tab => this.setTab(tab))
    );
  }

  discardChanges(): Observable<Tab | null> {
    const tab = this._tab$.value;
    if (!tab || !this._originalSnapshot) return of(null);
    return this.api.put<Tab>(`/api/tabs/${tab.tabId}/items`, this._originalSnapshot).pipe(
      tap(restored => {
        this._isExistingTab = false;
        this._originalSnapshot = null;
        this.setTab(restored);
        this.clearTab();
      })
    );
  }

  closeTab(tabId: string) {
    return this.api.delete<void>(`/api/tabs/${tabId}`).pipe(
      tap(() => this.clearTab())
    );
  }

  applyTabUpdate(tab: Tab): void {
    this.setTab(tab);
  }

  parkTab(): void {
    this._isExistingTab = false;
    this._originalSnapshot = null;
    this.clearTab();
  }

  private setTab(tab: Tab): void {
    this._tab$.next(tab);
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(tab));
  }

  clearTab(): void {
    this._tab$.next(null);
    sessionStorage.removeItem(SESSION_KEY);
  }

  private loadFromSession(): Tab | null {
    try {
      const raw = sessionStorage.getItem(SESSION_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
