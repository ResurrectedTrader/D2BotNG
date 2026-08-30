/**
 * Item search across every wire schema v2 capture.
 *
 * v2 only, and not because v1 was overlooked: only a v2 capture stores an item's stat lists, so
 * only v2 can be searched BY STAT. A v1 character sends the tooltip the game already rendered,
 * which is a string — there is nothing there to compare a bound against.
 *
 * Laid out the way ResurrectedTrade and the Path of Exile trade site are: what the item IS on the
 * left, the modifier groups on the right, and the results below. Nothing is searched until asked —
 * the filters build a request and "Search" sends it.
 */

import { useCallback, useEffect, useMemo, useRef } from "react";
import clsx from "clsx";
import {
  ChevronDownIcon,
  ChevronUpIcon,
  MagnifyingGlassIcon,
} from "@heroicons/react/24/outline";
import { Button, Card, CardContent, EmptyState } from "@/components/ui";
import { CtrlBreakdownHint } from "@/features/items";
import { useToolkit } from "@/hooks/useToolkit";
import { useCtrlWheelScroll } from "@/hooks/useCtrlWheelScroll";
import { searchRejectionMessage, useItemSearch } from "@/hooks/useItemSearch";
import { buildStatCatalog } from "./statCatalog";
import {
  buildSearchRequest,
  countedGroupRefused,
  requestHasFilter,
  DEFAULT_SORT,
  type SortChoice,
} from "./searchRequest";
import { useItemSearchState, type CommittedFilters } from "./searchState";
import { StatGroups } from "./StatGroups";
import { PropertyFilterPanel } from "./PropertyFilters";
import { SearchResults } from "./SearchResults";
import { Panel } from "./controls";

const PAGE_SIZE = 48;

export function ItemSearchTab({ profiles }: { profiles: string[] }) {
  // Ctrl is this view's breakdown key, so Ctrl+scroll here means "read on down the results" rather
  // than "zoom the app". The scrolling still happens; only the zooming is taken away.
  const wheelRef = useCtrlWheelScroll<HTMLDivElement>();

  // The tables are needed for the catalogue itself, so this tab always wants them.
  const engine = useToolkit(true);

  /**
   * Held in a store rather than in this component, because the tab that is not showing is
   * unmounted — see `searchState.ts`. Everything below behaves as it would as local state.
   *
   * `filtersOpen` folds the two filter panels away once a search has run: they are a lot of
   * controls and the results are what the reader came for. `submitted` is the request actually
   * sent, committed on Search rather than derived from the filters, so editing does not fire a
   * query per keystroke. `committed` is what that request was built from — re-sorting rebuilds it,
   * and rebuilding from the LIVE filters would silently answer a different question than the
   * results being looked at.
   */
  const {
    properties,
    setProperties,
    groups,
    setGroups,
    sort,
    setSort,
    filtersOpen,
    setFiltersOpen,
    submitted,
    setSubmitted,
    committed,
    setCommitted,
    reset,
  } = useItemSearchState();

  const catalog = useMemo(
    () => (engine ? buildStatCatalog(engine) : []),
    [engine],
  );

  const {
    data,
    isFetching,
    isFetchingNextPage,
    hasNextPage,
    fetchNextPage,
    error,
  } = useItemSearch(submitted);
  const rejection = searchRejectionMessage(error);

  /**
   * What Search would send, built from the live filters so the button can be gated on the REQUEST
   * rather than on the state it came from. The builder omits what it cannot express, so a panel
   * that looks filtered is no proof that anything would reach the wire — see `requestHasFilter`.
   */
  const pending = useMemo(
    // Offset 0: the hook owns paging from here, asking for the next slice as the list is scrolled.
    () => buildSearchRequest(properties, groups, sort, 0, PAGE_SIZE),
    [properties, groups, sort],
  );

  /** Search: commits what is on the filter panels, and everything after that re-reads it. */
  const runSearch = () => {
    setCommitted({ properties, groups });
    setFiltersOpen(false);
    setSubmitted(pending);
  };

  const rerun = useCallback(
    (filters: CommittedFilters, nextSort: SortChoice) => {
      // Offset 0: the hook owns paging from here, asking for the next slice as the list is
      // scrolled.
      setSubmitted(
        buildSearchRequest(
          filters.properties,
          filters.groups,
          nextSort,
          0,
          PAGE_SIZE,
        ),
      );
    },
    [setSubmitted],
  );

  /**
   * Re-sorting re-runs immediately — unlike a filter edit, which waits for Search. The order is a
   * property of the results on screen rather than of the query being composed, so leaving it staged
   * would show a list whose order contradicts what the header says it is. Everything loaded so far
   * is discarded with it: rows fetched under the old order are not a prefix of the new one.
   *
   * Held across renders because it is the result rows' only prop that is a closure, and they are
   * memoised against a page that re-renders on every capture any profile reports.
   */
  const changeSort = useCallback(
    (next: SortChoice) => {
      setSort(next);
      if (committed) rerun(committed, next);
    },
    [setSort, committed, rerun],
  );

  /** What the results are ordered by, or null for the store's own grouping order. */
  const sortLabel = sort.key.kind === "default" ? null : sort.key.label;

  // A counted group holding a row that no single condition can carry has no shape to send at all,
  // and leaving the row out would WIDEN an "at most" group. Refused here, where the row is still on
  // screen to be moved, rather than reinterpreted on the way to the store.
  const refusedGroup = groups.some(countedGroupRefused);
  const canSearch = requestHasFilter(pending) && !refusedGroup;
  // Only a NEW search occupies the button. `isFetching` also covers the slices infinite scroll
  // asks for, which would disable Search and read "Searching…" every time the reader scrolls.
  const isSearching = isFetching && !isFetchingNextPage;
  const matches = useMemo(
    () => (data?.pages ?? []).flatMap((p) => p.results),
    [data],
  );
  // Every page carries the full count, so the first one already knows how many there are.
  const total = data?.pages[0]?.total ?? 0;
  // A re-sort is a new cache key, so `data` is undefined until the response lands. Without this the
  // count read "No matches" and the list was replaced by the "nothing matched" panel for the length
  // of every search — a statement about the results made before any had arrived.
  const answered = data !== undefined;

  /**
   * Fetches the next slice when the end of the list comes into view.
   *
   * `rootMargin` starts it a screen early, so scrolling meets rows rather than a spinner. The
   * observer is rebuilt when the sentinel appears or the handlers change, and `hasNextPage` in the
   * deps is what tears it down once everything is loaded.
   */
  const sentinelRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel || !hasNextPage) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting && !isFetchingNextPage) {
          void fetchNextPage();
        }
      },
      { rootMargin: "600px" },
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage, matches.length]);

  return (
    <div ref={wheelRef} className="space-y-4">
      <CtrlBreakdownHint />
      <Card>
        <CardContent className="space-y-4">
          {/* Item on the left, modifiers on the right — the reference's split, and the one that
              matches how a search is composed: what the thing IS, then what it has to do.
              Unmounted rather than hidden when folded: these are two panels of live controls, and
              a hidden one still runs its ~1,230-entry catalogue filtering on every keystroke
              elsewhere. Their state lives above them, so nothing is lost by not rendering them. */}
          {filtersOpen && (
            <div className="grid items-start gap-4 xl:grid-cols-2">
              <Panel title="What the item is">
                <PropertyFilterPanel
                  filters={properties}
                  onChange={setProperties}
                  profiles={profiles}
                  engine={engine}
                />
              </Panel>

              <Panel title="What it has to have">
                {engine ? (
                  <StatGroups
                    catalog={catalog}
                    groups={groups}
                    onChange={setGroups}
                  />
                ) : (
                  <p className="text-sm text-zinc-500">
                    Loading the item tables…
                  </p>
                )}
              </Panel>
            </div>
          )}

          <div
            className={clsx(
              "flex items-center gap-3",
              filtersOpen && "border-t border-zinc-800 pt-3",
            )}
          >
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setFiltersOpen(!filtersOpen)}
              title={filtersOpen ? "Hide the filters" : "Show the filters"}
            >
              {filtersOpen ? (
                <ChevronUpIcon className="h-4 w-4" />
              ) : (
                <ChevronDownIcon className="h-4 w-4" />
              )}
              Filters
            </Button>
            <Button
              onClick={() => runSearch()}
              disabled={!canSearch || isSearching}
            >
              {isSearching ? "Searching…" : "Search"}
            </Button>
            {!canSearch && (
              <span className="text-xs text-zinc-500">
                {refusedGroup
                  ? "A counted group holds a modifier it cannot count. Move that row to a group with no count, or clear the count."
                  : "Add a filter — an unfiltered search would return every item of every character."}
              </span>
            )}
            <Button className="ml-auto" variant="secondary" onClick={reset}>
              Reset
            </Button>
          </div>
        </CardContent>
      </Card>

      {rejection && (
        <Card>
          <CardContent>
            {/* The store refuses a filter it cannot answer literally rather than repairing it,
                because a repaired filter matches more than was asked and the caller cannot tell.
                So this is a fixable statement about the filter, not an error. */}
            <p className="text-sm text-red-400">{rejection}</p>
          </CardContent>
        </Card>
      )}

      {error && !rejection && (
        <Card>
          <CardContent>
            <p className="text-sm text-red-400">
              {error instanceof Error ? error.message : "Search failed."}
            </p>
          </CardContent>
        </Card>
      )}

      {submitted && !error && (
        <Card>
          <CardContent className="space-y-3">
            <div className="flex items-center justify-between gap-3">
              <span className="text-xs text-zinc-400">
                {!answered
                  ? "Searching…"
                  : total === 0
                    ? "No matches"
                    : `${total.toLocaleString()} match${total === 1 ? "" : "es"}`}
                {/* There is no sort CONTROL: a modifier on a result is the handle, which is both
                    the reference's behaviour and one fewer panel. All that is left is saying what
                    the order currently is, and a way back out of it. */}
                {sortLabel && (
                  <>
                    {" · sorted by "}
                    <span className="text-zinc-200">{sortLabel}</span>
                    {sort.descending ? " ▼" : " ▲"}
                    <button
                      type="button"
                      onClick={() => changeSort(DEFAULT_SORT)}
                      className="ml-1 text-zinc-500 underline decoration-dotted hover:text-zinc-300"
                    >
                      clear
                    </button>
                  </>
                )}
              </span>
            </div>

            {/* Nothing at all until a response has landed, rather than the empty state: an answer
                about the results cannot be given before there are any, and on a re-sort that
                swapped a full list for "nothing matched" and back again. */}
            {!answered ? null : total === 0 ? (
              <EmptyState
                icon={MagnifyingGlassIcon}
                title="Nothing matched"
                description="Every group has to be satisfied at once. Loosening a bound, lowering a group's count, or dropping a modifier is usually what widens it."
              />
            ) : (
              <>
                <SearchResults
                  matches={matches}
                  engine={engine}
                  sort={sort}
                  onSort={changeSort}
                />
                {/* The end of the list, watched rather than clicked: reaching it IS the request
                    for more. Kept in the flow (not absolutely placed) so it moves down with the
                    rows it follows, and given a margin so the fetch starts a screen early and the
                    reader meets rows rather than a spinner. */}
                <div ref={sentinelRef} className="h-px" />
                {hasNextPage && (
                  <p className="py-2 text-center text-xs text-zinc-500">
                    {isFetchingNextPage
                      ? "Loading more…"
                      : `${matches.length.toLocaleString()} of ${total.toLocaleString()} shown`}
                  </p>
                )}
              </>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
