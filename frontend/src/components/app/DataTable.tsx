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
import { cn } from 'cn';
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react';
import { useId, useMemo, useState, type ChangeEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
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

export interface DataTableColumn<TRow extends RowData> {
  id: string;
  /** Already translated column title. */
  header: string;
  cell: (row: TRow) => ReactNode;
  /** Makes the column sortable by this value. */
  sortValue?: (row: TRow) => string | number;
  align?: 'start' | 'end';
}

export interface DataTableProps<TRow extends RowData> {
  /** Accessible name of the table, its scroll region and its pagination. */
  caption: string;
  /** Keep the reference stable (e.g. a TanStack Query result): a new array resets to page 1. */
  data: readonly TRow[];
  /** Keep the reference stable (module constant or useMemo): column definitions are derived from it. */
  columns: readonly DataTableColumn<TRow>[];
  getRowId: (row: TRow) => string;
  isLoading?: boolean;
  pageSize?: number;
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
}: DataTableProps<TRow>) {
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
          cell: (info) => column.cell(info.row.original),
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
    initialState: { pagination: { pageIndex: 0, pageSize } },
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

  return (
    <div className="flex flex-col gap-3">
      <div
        role="region"
        aria-labelledby={captionId}
        // A scrollable region must be keyboard-focusable (WCAG 2.1.1, axe scrollable-region-focusable).
        // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
        tabIndex={0}
        className="overflow-x-auto rounded-lg border bg-card"
      >
        <Table aria-busy={isLoading || undefined}>
          <TableCaption id={captionId} className="sr-only">
            {caption}
          </TableCaption>
          <TableHeader>
            {table.getHeaderGroups().map((group) => (
              <TableRow key={group.id}>
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
                  {columns.map((column) => (
                    <TableCell key={column.id}>
                      <Skeleton className="h-4 w-full" />
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            {!isLoading && total === 0 && (
              <TableRow>
                <TableCell colSpan={columns.length} className="py-10 text-center text-muted-foreground">
                  {t('table.empty')}
                </TableCell>
              </TableRow>
            )}
            {!isLoading &&
              table.getRowModel().rows.map((row) => (
                <TableRow key={row.id}>
                  {row.getAllCells().map((cell) => (
                    <TableCell key={cell.id} className={alignment(cell.column.id)}>
                      <table.FlexRender cell={cell} />
                    </TableCell>
                  ))}
                </TableRow>
              ))}
          </TableBody>
        </Table>
      </div>

      <p role="status" className="sr-only">
        {isLoading ? t('table.loading') : announcement}
      </p>

      {!isLoading && total > 0 && (
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
