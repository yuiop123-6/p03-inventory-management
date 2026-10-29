import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, describeFailure } from '../../core/api.service';
import { Category } from '../../core/api.models';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'app-categories',
  imports: [ReactiveFormsModule],
  template: `
    <div class="page-head">
      <div>
        <h1>Categories</h1>
        <p>Category names are unique ignoring case and surrounding spaces.</p>
      </div>
      <div class="page-actions">
        @if (auth.isAdmin()) {
          <button class="btn" type="button" (click)="openCreate()">New category</button>
        }
      </div>
    </div>

    <section class="card">
      @if (errorMessage()) {
        <div class="card-pad">
          <div class="alert alert-error" role="alert">{{ errorMessage() }}</div>
        </div>
      }

      @if (loading()) {
        <div class="card-pad">
          @for (row of [1, 2, 3]; track row) {
            <div class="skeleton" style="margin-bottom: 10px"></div>
          }
        </div>
      } @else if (categories().length === 0) {
        <div class="state">
          <strong>No categories yet</strong>
          <p>Create a category before adding products.</p>
        </div>
      } @else {
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Description</th>
                <th class="num">Products</th>
                @if (auth.isAdmin()) {
                  <th>Actions</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (category of categories(); track category.id) {
                <tr>
                  <td><strong>{{ category.name }}</strong></td>
                  <td class="muted">{{ category.description || '—' }}</td>
                  <td class="num">{{ category.productCount }}</td>
                  @if (auth.isAdmin()) {
                    <td>
                      <button class="btn btn-link btn-sm" type="button" (click)="openEdit(category)">Edit</button>
                      <button class="btn btn-link btn-sm" type="button" (click)="askDelete(category)">Delete</button>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>

    @if (editorOpen()) {
      <div class="modal-backdrop" (click)="closeEditor()">
        <div class="modal" role="dialog" aria-modal="true" aria-label="Category editor" (click)="$event.stopPropagation()">
          <div class="modal-head">
            <h2>{{ editingId === null ? 'New category' : 'Edit category' }}</h2>
            <button class="icon-btn" type="button" aria-label="Close" (click)="closeEditor()">×</button>
          </div>

          <form [formGroup]="form" (ngSubmit)="save()" novalidate>
            <div class="modal-body">
              <div class="field">
                <label for="categoryName">Name</label>
                <input id="categoryName" type="text" formControlName="name" />
                @if (form.controls.name.touched && form.controls.name.invalid) {
                  <span class="field-error">Required, max 100 characters.</span>
                }
              </div>

              <div class="field" style="margin-top: 14px">
                <label for="categoryDescription">Description</label>
                <textarea id="categoryDescription" formControlName="description" rows="3"></textarea>
              </div>

              @if (editorError()) {
                <div class="alert alert-error" role="alert" style="margin-top: 14px">{{ editorError() }}</div>
              }
            </div>

            <div class="modal-foot">
              <button class="btn btn-ghost" type="button" (click)="closeEditor()">Cancel</button>
              <button class="btn" type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save' }}</button>
            </div>
          </form>
        </div>
      </div>
    }

    @if (deleteTarget(); as category) {
      <div class="modal-backdrop" (click)="deleteTarget.set(null)">
        <div class="modal" role="dialog" aria-modal="true" aria-label="Delete category" (click)="$event.stopPropagation()">
          <div class="modal-head">
            <h2>Delete category</h2>
            <button class="icon-btn" type="button" aria-label="Close" (click)="deleteTarget.set(null)">×</button>
          </div>
          <div class="modal-body">
            <p>
              Delete <strong>{{ category.name }}</strong>? Categories that still contain products cannot be
              deleted.
            </p>
            @if (deleteError()) {
              <div class="alert alert-error" role="alert" style="margin-top: 12px">{{ deleteError() }}</div>
            }
          </div>
          <div class="modal-foot">
            <button class="btn btn-ghost" type="button" (click)="deleteTarget.set(null)">Cancel</button>
            <button class="btn btn-danger" type="button" [disabled]="deleting()" (click)="confirmDelete(category)">
              {{ deleting() ? 'Deleting…' : 'Delete' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class CategoriesComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly categories = signal<Category[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly editorOpen = signal(false);
  protected readonly editorError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected editingId: number | null = null;

  protected readonly deleteTarget = signal<Category | null>(null);
  protected readonly deleteError = signal<string | null>(null);
  protected readonly deleting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: [''],
  });

  ngOnInit(): void {
    this.load();
  }

  protected openCreate(): void {
    this.editingId = null;
    this.editorError.set(null);
    this.form.reset({ name: '', description: '' });
    this.editorOpen.set(true);
  }

  protected openEdit(category: Category): void {
    this.editingId = category.id;
    this.editorError.set(null);
    this.form.reset({ name: category.name, description: category.description ?? '' });
    this.editorOpen.set(true);
  }

  protected closeEditor(): void {
    if (this.saving()) {
      return;
    }

    this.editorOpen.set(false);
  }

  protected save(): void {
    if (this.saving()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, description } = this.form.getRawValue();
    const payload = { name: name.trim(), description: description.trim() || null };

    this.saving.set(true);
    this.editorError.set(null);

    const request =
      this.editingId === null
        ? this.api.createCategory(payload)
        : this.api.updateCategory(this.editingId, payload);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.editorOpen.set(false);
        this.toast.success('Category saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.editorError.set(describeFailure(error));
      },
    });
  }

  protected askDelete(category: Category): void {
    this.deleteError.set(null);
    this.deleteTarget.set(category);
  }

  protected confirmDelete(category: Category): void {
    if (this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.deleteError.set(null);

    this.api.deleteCategory(category.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.deleteTarget.set(null);
        this.toast.success(`Category "${category.name}" deleted.`);
        this.load();
      },
      error: (error: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(describeFailure(error));
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.api.getCategories().subscribe({
      next: (categories) => {
        this.categories.set(categories);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(describeFailure(error));
        this.loading.set(false);
      },
    });
  }
}
