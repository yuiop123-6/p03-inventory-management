import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  type: 'success' | 'error' | 'info';
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private sequence = 0;

  readonly toasts = signal<Toast[]>([]);

  success(message: string): void {
    this.push('success', message);
  }

  error(message: string): void {
    this.push('error', message);
  }

  info(message: string): void {
    this.push('info', message);
  }

  dismiss(id: number): void {
    this.toasts.update((items) => items.filter((item) => item.id !== id));
  }

  private push(type: Toast['type'], message: string): void {
    const id = ++this.sequence;
    this.toasts.update((items) => [...items, { id, type, message }]);

    setTimeout(() => this.dismiss(id), type === 'error' ? 6000 : 3500);
  }
}
