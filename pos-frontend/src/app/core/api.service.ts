import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(path: string): Observable<T> {
    return this.http.get<T>(`${environment.apiBaseUrl}${path}`);
  }

  post<T>(path: string, body: unknown): Observable<T> {
    return this.http.post<T>(`${environment.apiBaseUrl}${path}`, body);
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(`${environment.apiBaseUrl}${path}`);
  }
}
