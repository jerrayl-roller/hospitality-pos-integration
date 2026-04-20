import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule, CurrencyPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { TabStateService } from '../../core/tab-state.service';
import { NotificationService } from '../../core/notification.service';
import { NewTabDialogComponent, NewTabResult } from '../tab/new-tab-dialog';

export interface Product {
  productId: string;
  name: string;
  parentName: string;
  price: number;
  productType: string;
  productSubType: string;
  category: string | null;
  imageUrl: string | null;
}

@Component({
  selector: 'app-catalogue',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, MatButtonModule, MatProgressSpinnerModule, MatIconModule],
  templateUrl: './catalogue.html',
  styleUrl: './catalogue.scss'
})
export class CatalogueComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly tabState = inject(TabStateService);
  private readonly notifications = inject(NotificationService);
  private readonly dialog = inject(MatDialog);

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
      next: products => {
        this.products.set(products);
        const cats = [...new Set(products.map(p => p.category ?? 'Uncategorised'))].sort();
        this.categories.set(cats);
        this.selectedCategory.set(cats[0] ?? null);
        this.loading.set(false);
      },
      error: () => { this.error.set(true); this.loading.set(false); }
    });
  }

  selectCategory(cat: string): void {
    this.selectedCategory.set(cat);
  }

  filteredProducts(): Product[] {
    const cat = this.selectedCategory();
    return this.products().filter(p => (p.category ?? 'Uncategorised') === cat);
  }

  displayName(product: Product): string {
    return product.parentName ? `${product.parentName} — ${product.name}` : product.name;
  }

  onProductClick(product: Product): void {
    const tab = this.tabState.currentTab;
    if (tab) {
      this.addItemToTab(tab.tabId, product);
    } else {
      this.promptNewTabThenAdd(product);
    }
  }

  private promptNewTabThenAdd(product: Product): void {
    const ref = this.dialog.open(NewTabDialogComponent, { width: '400px', disableClose: true });
    ref.afterClosed().subscribe((result: NewTabResult | null) => {
      if (!result) return;
      this.tabState.openNewTab(result).subscribe({
        next: tab => this.addItemToTab(tab.tabId, product),
        error: () => {}
      });
    });
  }

  private addItemToTab(tabId: string, product: Product): void {
    this.addingProductId.set(product.productId);
    this.tabState.addItem(tabId, {
      productId: product.productId,
      productName: this.displayName(product),
      quantity: 1,
      unitPrice: product.price
    }).subscribe({
      next: () => this.addingProductId.set(null),
      error: () => this.addingProductId.set(null)
    });
  }
}
