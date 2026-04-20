import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule, CurrencyPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { TabStateService } from '../../core/tab-state.service';
import { NotificationService } from '../../core/notification.service';

export interface Product {
  productId: string;
  name: string;
  price: number;
  productType: string;
  productSubType: string;
  category: string | null;
}

@Component({
  selector: 'app-catalogue',
  standalone: true,
  imports: [
    CommonModule,
    CurrencyPipe,
    MatCardModule,
    MatButtonModule,
    MatTabsModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatIconModule
  ],
  templateUrl: './catalogue.html',
  styleUrl: './catalogue.scss'
})
export class CatalogueComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly tabState = inject(TabStateService);
  private readonly notifications = inject(NotificationService);

  loading = signal(true);
  error = signal(false);
  products = signal<Product[]>([]);
  categories = signal<string[]>([]);
  selectedCategory = signal<string | null>(null);
  addingProductId = signal<string | null>(null);

  ngOnInit(): void {
    this.loadProducts();
  }

  loadProducts(): void {
    this.loading.set(true);
    this.error.set(false);

    this.api.get<Product[]>('/api/products/fnb').subscribe({
      next: (products) => {
        this.products.set(products);
        const cats = [...new Set(products.map(p => p.category ?? 'Uncategorised'))].sort();
        this.categories.set(cats);
        this.selectedCategory.set(cats[0] ?? null);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      }
    });
  }

  selectCategory(cat: string): void {
    this.selectedCategory.set(cat);
  }

  filteredProducts(): Product[] {
    const cat = this.selectedCategory();
    return this.products().filter(p => (p.category ?? 'Uncategorised') === cat);
  }

  addToTab(product: Product): void {
    const tab = this.tabState.currentTab;
    if (!tab) return;

    this.addingProductId.set(product.productId);
    this.tabState.addItem(tab.tabId, {
      productId: product.productId,
      productName: product.name,
      quantity: 1,
      unitPrice: product.price
    }).subscribe({
      next: () => this.addingProductId.set(null),
      error: () => this.addingProductId.set(null)
    });
  }
}
