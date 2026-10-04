import {
  createColumnHelper,
  createPaginatedRowModel,
  createSortedRowModel,
  rowPaginationFeature,
  rowSortingFeature,
  tableFeatures,
  useTable,
  type RowData,
} from '@tanstack/react-table';
import { cn } from '@/lib/cn';
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react';
import { useId, useMemo, useState, type ChangeEvent, type MouseEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useIsMobile } from '@/hooks/use-mobile';

export interface DataTableColumn<TRow extends RowData> {
  id: string;
  /** Already translated column title. */
  header: string;
  cell: (row: TRow) => ReactNode;
  /** A second, quieter line under the cell (e.g. the nationalId under the name). */
  secondary?: (row: TRow) => ReactNode;
  /** Makes the column sortable by this value. */
  sortValue?: (row: TRow) => string | number;
  align?: 'start' | 'end';
  /** Keeps the header for screen readers only, e.g. "Actions" above a column of row buttons. */
  hideHeader?: boolean;
  /**
   * The column that names the row (e.g. the name), rendered as a row header so screen readers
   * announce it with each cell of the row. At most one column.
   */
  rowHeader?: boolean;
}

/** Selected rows by row id (`getRowId`); ids of rows not in `data` (filtered out, other pages) are kept. */
export type RowSelection = Readonly<Record<string, boolean>>;

interface DataTableBaseProps<TRow extends RowData> {
  /** Accessible name of the table, its scroll region and its pagination. */
  caption: string;
  /** Keep the reference stable (e.g. a TanStack Query result): a new array resets to page 1. */
  data: readonly TRow[];
  /** Keep the reference stable (module constant or useMemo): column definitions are derived from it. */
  columns: readonly DataTableColumn<TRow>[];
  getRowId: (row: TRow) => string;
  isLoading?: boolean;
  pageSize?: number;
  /** False for a short list (a handful of rows): every row, no pagination controls. Read once, at mount. */
  paginated?: boolean;
  /** Already translated text for an empty list; the generic "No results" otherwise. */
  emptyText?: string;
  /**
   * The record a row leads to. Its cells must contain a link to it (usually the name), which stays
   * the row's only tab stop; a click anywhere else on the row follows that link.
   */
  getRowHref?: (row: TRow) => string;
  /** The row as a stacked item on phones (below 768 px), instead of the table. */
  mobileRow?: (row: TRow) => ReactNode;
}

/**
 * Row selection, owned by the screen (spec: Data tables): all three props or none. Each row gets a
 * checkbox and the current page one. The screen keeps ids of rows that are filtered out or on other
 * pages, and removes the ids of rows that no longer exist (e.g. after a reload).
 */
type DataTableSelectionProps<TRow extends RowData> =
  | {
      rowSelection: RowSelection;
      onRowSelectionChange: (selection: RowSelection) => void;
      /** The row's name for its checkbox label ("Select …"). */
      getRowLabel: (row: TRow) => string;
    }
  | { rowSelection?: never; onRowSelectionChange?: never; getRowLabel?: never };

export type DataTableProps<TRow extends RowData> = DataTableBaseProps<TRow> & DataTableSelectionProps<TRow>;

/** Elements whose own click must not open the row's record, the selection cell included (a near miss on its box). */
const INTERACTIVE =
  'a, button, input, select, textarea, label, summary, [contenteditable], [tabindex], [role="button"], [role="menuitem"], [role="checkbox"], [role="switch"], [role="tab"], [data-row-select]';

/**
 * Follows the row's link to its record (spec: Data tables, whole row opens the record), unless the
 * click was on a control of the row, came from a dialog or menu opened from the row (portals
 * bubble through React), used a modifier key or another button (the link itself handles those),
 * or ended a text selection inside the row.
 */
function openRecord(event: MouseEvent<HTMLElement>, href: string): void {
  const row = event.currentTarget;
  const target = event.target as Node;
  if (event.defaultPrevented || !row.contains(target)) return;
  if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  const control = target instanceof Element ? target.closest(INTERACTIVE) : null;
  if (control && row.contains(control)) return;
  const selection = window.getSelection();
  if (selection && !selection.isCollapsed && selection.anchorNode && row.contains(selection.anchorNode))
    return;
  const link = [...row.querySelectorAll<HTMLAnchorElement>('a[href]')].find(
    (candidate) => candidate.getAttribute('href') === href,
  );
  link?.click();
}

const PAGE_SIZES = [10, 20, 50] as const;
const LOADING_ROWS = 5;

const features = tableFeatures({
  rowSortingFeature,
  rowPaginationFeature,
  sortedRowModel: createSortedRowModel(),
  paginatedRowModel: createPaginatedRowModel(),
});

type AriaSort = 'ascending' | 'descending' | 'none';

/**
 * Sortable, paginated list with loading and empty states (spec: Data tables). Wide tables scroll
 * inside a focusable region so the page never scrolls horizontally.
 */
export function DataTable<TRow extends RowData>({
  caption,
  data,
  columns,
  getRowId,
  isLoading = false,
  pageSize = PAGE_SIZES[1],
  paginated = true,
  emptyText,
  getRowHref,
  mobileRow,
  rowSelection,
  onRowSelectionChange,
  getRowLabel,
}: DataTableProps<TRow>) {
  const stacked = useIsMobile() && mobileRow !== undefined;
  const pageSizeId = useId();
  const captionId = useId();

  const { t, i18n } = useTranslation('ui');
  const [announcement, setAnnouncement] = useState('');

  const { columnDefs, byId } = useMemo(() => {
    // Locale-aware ordering: accents and ñ sort as in the active language.
    const collator = new Intl.Collator(i18n.language, { numeric: true, sensitivity: 'base' });
    const helper = createColumnHelper<typeof features, TRow>();
    const defs = helper.columns(
      columns.map((column) =>
        helper.accessor((row) => column.sortValue?.(row) ?? '', {
          id: column.id,
          header: column.header,
          enableSorting: column.sortValue !== undefined,
          sortFn: (a, b, id) => {
            const x = a.getValue<string | number>(id);
            const y = b.getValue<string | number>(id);
            return typeof x === 'number' && typeof y === 'number'
              ? x - y
              : collator.compare(String(x), String(y));
          },
          cell: (info) => {
            const secondary = column.secondary?.(info.row.original);
            return secondary === undefined ? (
              column.cell(info.row.original)
            ) : (
              <div className="flex flex-col gap-0.5">
                <span>{column.cell(info.row.original)}</span>
                <span className="text-help text-muted-foreground">{secondary}</span>
              </div>
            );
          },
        }),
      ),
    );
    return { columnDefs: defs, byId: new Map(columns.map((column) => [column.id, column])) };
  }, [columns, i18n.language]);

  const table = useTable({
    features,
    columns: columnDefs,
    data,
    getRowId,
    initialState: { pagination: { pageIndex: 0, pageSize: paginated ? pageSize : Number.MAX_SAFE_INTEGER } },
  });

  const { pageIndex, pageSize: currentPageSize } = table.state.pagination;
  const total = data.length;
  const pageCount = Math.max(1, Math.ceil(total / currentPageSize));
  // Data may shrink below the current page; show the last valid page meanwhile.
  const safePageIndex = Math.min(pageIndex, pageCount - 1);
  if (safePageIndex !== pageIndex) {
    table.setPageIndex(safePageIndex);
  }
  const from = safePageIndex * currentPageSize + 1;
  const to = Math.min(total, from + currentPageSize - 1);
  const summary = () => t('table.pagination.summary', { from, to, total });
  const alignment = (id: string) => (byId.get(id)?.align === 'end' ? 'text-end' : 'text-start');
  const sortLabel = (sorted: false | 'asc' | 'desc') =>
    sorted === 'asc'
      ? t('table.sortAscending')
      : sorted === 'desc'
        ? t('table.sortDescending')
        : t('table.sortNone');

  const announcePage = (nextIndex: number, size = currentPageSize) => {
    const first = nextIndex * size + 1;
    setAnnouncement(
      t('table.pagination.summary', { from: first, to: Math.min(total, first + size - 1), total }),
    );
  };

  const onPageSize = (event: ChangeEvent<HTMLSelectElement>) => {
    const size = Number(event.target.value);
    table.setPageSize(size);
    announcePage(0, size);
  };

  const rows = table.getRowModel().rows;

  // Selection is kept by id across pages, sorting and filters; the header box acts on this page only.
  const selectable = rowSelection !== undefined;
  const isSelected = (id: string) => rowSelection?.[id] === true;
  const pageSelected = rows.filter((row) => isSelected(row.id)).length;
  const pageState: boolean | 'indeterminate' =
    rows.length > 0 && pageSelected === rows.length ? true : pageSelected > 0 ? 'indeterminate' : false;
  const changeSelection = (next: Record<string, boolean>) => {
    const kept = Object.fromEntries(Object.entries(next).filter(([, value]) => value));
    onRowSelectionChange?.(kept);
    setAnnouncement(t('table.selection.count', { count: Object.keys(kept).length }));
  };
  const toggleRow = (id: string, checked: boolean) => {
    changeSelection({ ...rowSelection, [id]: checked });
  };
  const togglePage = () => {
    const checked = pageState !== true;
    changeSelection({ ...rowSelection, ...Object.fromEntries(rows.map((row) => [row.id, checked])) });
  };
  const rowCheckbox = (row: (typeof rows)[number]) => (
    <Checkbox
      checked={isSelected(row.id)}
      aria-label={t('table.selection.row', { name: getRowLabel?.(row.original) })}
      onCheckedChange={(value) => {
        toggleRow(row.id, value === true);
      }}
    />
  );
  const columnCount = columns.length + (selectable ? 1 : 0);
  const pageCheckbox = (id?: string) => (
    <Checkbox
      id={id}
      checked={pageState}
      disabled={isLoading || rows.length === 0}
      aria-label={id === undefined ? t('table.selection.page') : undefined}
      onCheckedChange={togglePage}
    />
  );
  const pageCheckboxId = `${captionId}-page`;

  return (
    <div className="flex flex-col gap-3">
      {stacked && selectable && (
        <div className="flex items-center gap-3 px-4">
          {pageCheckbox(pageCheckboxId)}
          <Label htmlFor={pageCheckboxId} className="font-normal">
            {t('table.selection.page')}
          </Label>
        </div>
      )}
      {stacked && (
        // role="list": Safari drops list semantics from lists without bullets.
        // eslint-disable-next-line jsx-a11y/no-redundant-roles
        <ul
          role="list"
          aria-label={caption}
          aria-busy={isLoading || undefined}
          className="flex flex-col gap-2"
        >
          {isLoading &&
            Array.from({ length: LOADING_ROWS }, (_, index) => (
              <li key={index} className="flex flex-col gap-2 rounded-lg border bg-card px-4 py-3">
                <Skeleton className="h-4 w-1/2" />
                <Skeleton className="h-3 w-3/4" />
              </li>
            ))}
          {!isLoading && total === 0 && (
            <li className="rounded-lg border bg-card px-4 py-10 text-center text-muted-foreground">
              {emptyText ?? t('table.empty')}
            </li>
          )}
          {!isLoading &&
            rows.map((row) => {
              const href = getRowHref?.(row.original);
              return (
                // A pointer shortcut only: keyboard users reach the record through the row's link.
                // eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-noninteractive-element-interactions
                <li
                  key={row.id}
                  onClick={
                    href === undefined
                      ? undefined
                      : (event) => {
                          openRecord(event, href);
                        }
                  }
                  data-state={selectable && isSelected(row.id) ? 'selected' : undefined}
                  className={cn(
                    'flex flex-col gap-1 rounded-lg border bg-card px-4 py-3 shadow-e1',
                    href !== undefined && 'cursor-pointer hover:bg-surface-2',
                    'data-[state=selected]:bg-muted data-[state=selected]:hover:bg-muted',
                  )}
                >
                  {selectable ? (
                    <div className="flex items-start gap-3">
                      <span data-row-select="" className="pt-0.5">
                        {rowCheckbox(row)}
                      </span>
                      <div className="flex min-w-0 flex-col gap-1">{mobileRow(row.original)}</div>
                    </div>
                  ) : (
                    mobileRow(row.original)
                  )}
                </li>
              );
            })}
        </ul>
      )}
      {!stacked && (
        <Table
          aria-busy={isLoading || undefined}
          // The scroll container is the named region; a scrollable region must be keyboard-focusable
          // (WCAG 2.1.1, axe scrollable-region-focusable).
          container={{
            role: 'region',
            'aria-labelledby': captionId,
            tabIndex: 0,
            className: 'rounded-lg border bg-card',
          }}
        >
          <TableCaption id={captionId} className="sr-only">
            {caption}
          </TableCaption>
          <TableHeader>
            {table.getHeaderGroups().map((group) => (
              <TableRow key={group.id}>
                {selectable && <TableHead className="w-10">{pageCheckbox()}</TableHead>}
                {group.headers.map((header) => {
                  const sortable = header.column.getCanSort();
                  const sorted = header.column.getIsSorted();
                  const ariaSort: AriaSort | undefined = sortable
                    ? sorted === 'asc'
                      ? 'ascending'
                      : sorted === 'desc'
                        ? 'descending'
                        : 'none'
                    : undefined;
                  const SortIcon = sorted === 'asc' ? ArrowUp : sorted === 'desc' ? ArrowDown : ArrowUpDown;
                  const title = String(header.column.columnDef.header);
                  return (
                    <TableHead key={header.id} aria-sort={ariaSort} className={alignment(header.column.id)}>
                      {sortable ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="-mx-2"
                          onClick={() => {
                            const next = sorted === false ? 'asc' : sorted === 'asc' ? 'desc' : false;
                            header.column.toggleSorting(next === 'desc', false);
                            if (next === false) header.column.clearSorting();
                            setAnnouncement(
                              next === false
                                ? t('table.announce.unsorted')
                                : t('table.announce.sorted', { column: title, direction: sortLabel(next) }),
                            );
                          }}
                        >
                          {title}
                          <span className="sr-only">{`, ${sortLabel(sorted)}`}</span>
                          <SortIcon aria-hidden="true" className={cn(!sorted && 'text-muted-foreground')} />
                        </Button>
                      ) : byId.get(header.column.id)?.hideHeader ? (
                        <span className="sr-only">{title}</span>
                      ) : (
                        title
                      )}
                    </TableHead>
                  );
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {isLoading &&
              Array.from({ length: LOADING_ROWS }, (_, index) => (
                <TableRow key={index}>
                  {selectable && <TableCell />}
                  {columns.map((column) => (
                    <TableCell key={column.id}>
                      <Skeleton className="h-4 w-full" />
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            {!isLoading && total === 0 && (
              <TableRow>
                <TableCell colSpan={columnCount} className="py-10 text-center text-muted-foreground">
                  {emptyText ?? t('table.empty')}
                </TableCell>
              </TableRow>
            )}
            {!isLoading &&
              rows.map((row) => {
                const href = getRowHref?.(row.original);
                return (
                  <TableRow
                    key={row.id}
                    // A pointer shortcut only: keyboard users reach the record through the row's link.
                    onClick={
                      href === undefined
                        ? undefined
                        : (event) => {
                            openRecord(event, href);
                          }
                    }
                    data-state={selectable && isSelected(row.id) ? 'selected' : undefined}
                    className={cn('h-row', href !== undefined && 'cursor-pointer')}
                  >
                    {selectable && (
                      <TableCell data-row-select="" className="w-10">
                        {rowCheckbox(row)}
                      </TableCell>
                    )}
                    {row.getAllCells().map((cell) =>
                      byId.get(cell.column.id)?.rowHeader ? (
                        <th
                          key={cell.id}
                          scope="row"
                          data-slot="table-cell"
                          className={cn(
                            'p-2 align-middle font-normal whitespace-nowrap',
                            alignment(cell.column.id),
                          )}
                        >
                          <table.FlexRender cell={cell} />
                        </th>
                      ) : (
                        <TableCell key={cell.id} className={alignment(cell.column.id)}>
                          <table.FlexRender cell={cell} />
                        </TableCell>
                      ),
                    )}
                  </TableRow>
                );
              })}
          </TableBody>
        </Table>
      )}

      <p role="status" className="sr-only">
        {isLoading ? t('table.loading') : announcement}
      </p>

      {paginated && !isLoading && total > 0 && (
        <nav
          aria-label={t('table.pagination.label', { caption })}
          className="flex flex-wrap items-center justify-between gap-3 text-sm"
        >
          <div className="flex items-center gap-2">
            <label htmlFor={pageSizeId} className="text-muted-foreground">
              {t('table.pagination.pageSize')}
            </label>
            <NativeSelect id={pageSizeId} value={String(currentPageSize)} onChange={onPageSize}>
              {PAGE_SIZES.map((size) => (
                <NativeSelectOption key={size} value={String(size)}>
                  {size}
                </NativeSelectOption>
              ))}
            </NativeSelect>
          </div>
          <div className="flex items-center gap-2">
            <span className="text-muted-foreground tabular-nums">{summary()}</span>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                table.previousPage();
                announcePage(safePageIndex - 1);
              }}
              disabled={!table.getCanPreviousPage()}
            >
              {t('table.pagination.previous')}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                table.nextPage();
                announcePage(safePageIndex + 1);
              }}
              disabled={!table.getCanNextPage()}
            >
              {t('table.pagination.next')}
            </Button>
          </div>
        </nav>
      )}
    </div>
  );
}
