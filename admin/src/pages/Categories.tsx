import { useMemo, useState, type FormEvent } from 'react'
import {
  DndContext,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import {
  SortableContext,
  arrayMove,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  useCategories,
  useCreateCategory,
  useDeleteCategory,
  useReorderCategories,
  useUpdateCategory,
  type Category,
  type CategoryInput,
} from '../api/categories'

/* ── forma dialoqu ─────────────────────────────────── */

interface EditorState {
  mode: 'create' | 'edit'
  parentId: string | null
  category?: Category
}

function CategoryEditor({
  state,
  onClose,
}: {
  state: EditorState
  onClose: () => void
}) {
  const create = useCreateCategory()
  const update = useUpdateCategory()
  const busy = create.isPending || update.isPending
  const error = create.error ?? update.error

  const [name, setName] = useState(state.category?.name ?? '')
  const [description, setDescription] = useState(state.category?.description ?? '')
  const [codePrefix, setCodePrefix] = useState(state.category?.codePrefix ?? '')

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    const payload: CategoryInput = {
      name,
      description: description || null,
      codePrefix: codePrefix || null,
    }
    if (state.mode === 'create') {
      create.mutate({ ...payload, parentId: state.parentId }, { onSuccess: onClose })
    } else {
      update.mutate({ id: state.category!.id, ...payload }, { onSuccess: onClose })
    }
  }

  return (
    <div className="fixed inset-0 z-10 flex items-center justify-center bg-stone-900/40 px-4">
      <form
        onSubmit={onSubmit}
        className="w-full max-w-md rounded-lg border border-stone-200 bg-white p-6 shadow-lg"
      >
        <h2 className="text-base font-semibold">
          {state.mode === 'create' ? 'Yeni kateqoriya' : 'Kateqoriyanı redaktə et'}
        </h2>

        <label className="mt-4 block">
          <span className="text-sm font-medium text-stone-700">Ad</span>
          <input
            required
            value={name}
            onChange={e => setName(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
          />
        </label>

        <label className="mt-3 block">
          <span className="text-sm font-medium text-stone-700">Təsvir (istəyə görə)</span>
          <textarea
            value={description}
            onChange={e => setDescription(e.target.value)}
            rows={2}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
          />
        </label>

        <label className="mt-3 block">
          <span className="text-sm font-medium text-stone-700">Kod prefiksi (istəyə görə)</span>
          <input
            value={codePrefix}
            onChange={e => setCodePrefix(e.target.value.toUpperCase())}
            maxLength={4}
            placeholder="SZ"
            className="mt-1 w-24 rounded border border-stone-300 px-3 py-2 font-mono text-sm uppercase outline-none focus:border-emerald-700"
          />
          <span className="mt-1 block text-xs text-stone-400">
            QR kodlarının insan-oxunan hissəsi üçün: SZ-0142. 2–4 böyük hərf.
          </span>
        </label>

        {error && (
          <p role="alert" className="mt-3 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
            {error.message}
          </p>
        )}

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded border border-stone-300 px-3 py-1.5 text-sm hover:bg-stone-50"
          >
            İmtina
          </button>
          <button
            type="submit"
            disabled={busy}
            className="rounded bg-emerald-800 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60"
          >
            {busy ? 'Saxlanılır…' : 'Saxla'}
          </button>
        </div>
      </form>
    </div>
  )
}

/* ── ağac sətri ────────────────────────────────────── */

function CategoryRow({
  category,
  childCount,
  onAddChild,
  onEdit,
  onDelete,
}: {
  category: Category
  childCount: number
  onAddChild: () => void
  onEdit: () => void
  onDelete: () => void
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: category.id })

  return (
    <div
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={`flex items-center gap-2 rounded border border-stone-200 bg-white px-3 py-2 ${
        isDragging ? 'opacity-60 shadow-md' : ''
      }`}
    >
      <button
        type="button"
        {...attributes}
        {...listeners}
        aria-label="Sürüşdürərək sırala"
        className="cursor-grab touch-none text-stone-300 hover:text-stone-500"
      >
        ⠿
      </button>
      <span className="flex-1 text-sm">{category.name}</span>
      {category.codePrefix && (
        <span className="rounded border border-emerald-200 bg-emerald-50 px-1.5 py-0.5 font-mono text-xs text-emerald-900">
          {category.codePrefix}
        </span>
      )}
      {childCount > 0 && (
        <span className="text-xs text-stone-400">{childCount} alt</span>
      )}
      <button
        type="button"
        onClick={onAddChild}
        className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100"
      >
        + alt
      </button>
      <button
        type="button"
        onClick={onEdit}
        className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100"
      >
        Redaktə
      </button>
      <button
        type="button"
        onClick={onDelete}
        className="rounded px-2 py-1 text-xs text-red-600 hover:bg-red-50"
      >
        Sil
      </button>
    </div>
  )
}

/* ── bir səviyyənin sıralana bilən siyahısı ────────── */

function CategoryLevel({
  parentId,
  categories,
  onOpenEditor,
  depth,
}: {
  parentId: string | null
  categories: Category[]
  onOpenEditor: (state: EditorState) => void
  depth: number
}) {
  const reorder = useReorderCategories()
  const remove = useDeleteCategory()
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }))

  const level = useMemo(
    () =>
      categories
        .filter(c => c.parentId === parentId)
        .sort((a, b) => a.sortOrder - b.sortOrder),
    [categories, parentId],
  )

  const [localOrder, setLocalOrder] = useState<string[] | null>(null)
  const ids = localOrder ?? level.map(c => c.id)
  const ordered = ids
    .map(id => level.find(c => c.id === id))
    .filter((c): c is Category => c !== undefined)

  function onDragEnd(event: DragEndEvent) {
    const { active, over } = event
    if (!over || active.id === over.id) return
    const next = arrayMove(ids, ids.indexOf(String(active.id)), ids.indexOf(String(over.id)))
    setLocalOrder(next)
    reorder.mutate(next, { onSettled: () => setLocalOrder(null) })
  }

  if (level.length === 0) return null

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
      <SortableContext items={ids} strategy={verticalListSortingStrategy}>
        <div className="space-y-1" style={{ marginLeft: depth * 24 }}>
          {ordered.map(category => (
            <div key={category.id}>
              <CategoryRow
                category={category}
                childCount={categories.filter(c => c.parentId === category.id).length}
                onAddChild={() =>
                  onOpenEditor({ mode: 'create', parentId: category.id })
                }
                onEdit={() =>
                  onOpenEditor({ mode: 'edit', parentId: category.parentId, category })
                }
                onDelete={() => {
                  if (window.confirm(`"${category.name}" silinsin?`)) remove.mutate(category.id)
                }}
              />
              <div className="mt-1">
                <CategoryLevel
                  parentId={category.id}
                  categories={categories}
                  onOpenEditor={onOpenEditor}
                  depth={depth + 1}
                />
              </div>
            </div>
          ))}
        </div>
      </SortableContext>
      {remove.isError && (
        <p role="alert" className="mt-2 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {remove.error.message}
        </p>
      )}
    </DndContext>
  )
}

/* ── səhifə ────────────────────────────────────────── */

export default function Categories() {
  const categories = useCategories()
  const [editor, setEditor] = useState<EditorState | null>(null)

  return (
    <div>
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold tracking-tight">Kateqoriyalar</h1>
          <p className="mt-1 text-sm text-stone-500">
            Sürüşdürərək sırala; alt-kateqoriya üçün «+ alt» düyməsi.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setEditor({ mode: 'create', parentId: null })}
          className="rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700"
        >
          Yeni kateqoriya
        </button>
      </div>

      <div className="mt-6">
        {categories.isPending && <p className="text-sm text-stone-500">Yüklənir…</p>}
        {categories.isError && (
          <p role="alert" className="rounded bg-red-50 px-3 py-2 text-sm text-red-800">
            {categories.error.message}
          </p>
        )}
        {categories.data && categories.data.length === 0 && (
          <p className="rounded border border-dashed border-stone-300 px-4 py-8 text-center text-sm text-stone-400">
            Hələ kateqoriya yoxdur — «Yeni kateqoriya» ilə başlayın.
          </p>
        )}
        {categories.data && categories.data.length > 0 && (
          <CategoryLevel
            parentId={null}
            categories={categories.data}
            onOpenEditor={setEditor}
            depth={0}
          />
        )}
      </div>

      {editor && (
        <CategoryEditor
          key={`${editor.mode}-${editor.category?.id ?? editor.parentId ?? 'root'}`}
          state={editor}
          onClose={() => setEditor(null)}
        />
      )}
    </div>
  )
}
