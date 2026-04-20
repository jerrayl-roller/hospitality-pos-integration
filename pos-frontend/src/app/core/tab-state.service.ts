import { Injectable, inject } from '@angular/core';
import { BehaviorSubject, tap } from 'rxjs';
import { ApiService } from './api.service';

export interface TabLineItem {
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
}

export interface Tab {
  tabId: string;
  bookingId: string | null;
  addedItems: TabLineItem[];
  grandTotal: number;
  paymentStatus: string;
  preAuthStatus: string;
  preAuthCardNumber: string | null;
  hasPendingConflict: boolean;
  openedAt: string;
  settledAt: string | null;
}

const SESSION_KEY = 'pos_active_tab';

@Injectable({ providedIn: 'root' })
export class TabStateService {
  private readonly api = inject(ApiService);
  private readonly _tab$ = new BehaviorSubject<Tab | null>(this.loadFromSession());

  readonly tab$ = this._tab$.asObservable();

  get currentTab(): Tab | null {
    return this._tab$.value;
  }

  openNewTab() {
    return this.api.post<Tab>('/api/tabs', {}).pipe(
      tap(tab => this.setTab(tab))
    );
  }

  refreshTab(tabId: string) {
    return this.api.get<Tab>(`/api/tabs/${tabId}`).pipe(
      tap(tab => this.setTab(tab))
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

  closeTab(tabId: string) {
    return this.api.delete<void>(`/api/tabs/${tabId}`).pipe(
      tap(() => this.clearTab())
    );
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
