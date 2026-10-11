/**
 * What the item search is currently asking, held outside the component that renders it.
 *
 * The tabs unmount the panel that is not showing, so everything a search consists of — the filters,
 * the committed request, the sort, whether the filter panels are folded — died on a glance at the
 * character viewer and had to be typed again. Storing it here makes a tab switch (and a trip to
 * another page) leave the search where it was.
 *
 * Only the QUESTION lives here. The results do not: React Query already caches them under the
 * request, so a remount with the same `submitted` gets its pages back without a round trip, and
 * keeping a second copy of a few hundred items would be a copy to invalidate.
 *
 * A store rather than state lifted into `CharactersPage` because the panel owns all of this and
 * none of its siblings care; passing six values and six setters through the page would put the
 * search's shape in a component that has nothing to do with it.
 */

import { create } from "zustand";
import type { GameVariant, SearchItemsRequest } from "@/generated/captures_pb";
import {
  DEFAULT_SORT,
  EMPTY_PROPERTIES,
  emptyStatGroup,
  type PropertyFilters,
  type SortChoice,
  type StatFilterGroup,
} from "./searchRequest";

/** The filters a submitted request was built from, kept so a re-sort can rebuild it. */
export interface CommittedFilters {
  properties: PropertyFilters;
  groups: StatFilterGroup[];
  variant: GameVariant;
}

interface ItemSearchState {
  /**
   * The game being searched, as the reader chose it; null until they do, which means "the one
   * there is" (see `ItemSearchTab`). Every filter below is written in that game's tables, which is
   * why choosing another one starts the search over.
   */
  variant: GameVariant | null;
  properties: PropertyFilters;
  groups: StatFilterGroup[];
  sort: SortChoice;
  filtersOpen: boolean;
  submitted: SearchItemsRequest | null;
  committed: CommittedFilters | null;

  /**
   * Switches game and clears everything else. The picks are rows of the old game's tables — a
   * base item, a type, a stat — and the same number names something else in the new one, so
   * carrying them across would quietly change what they ask for.
   */
  chooseVariant: (variant: GameVariant) => void;
  setProperties: (properties: PropertyFilters) => void;
  setGroups: (groups: StatFilterGroup[]) => void;
  setSort: (sort: SortChoice) => void;
  setFiltersOpen: (open: boolean) => void;
  setSubmitted: (request: SearchItemsRequest | null) => void;
  setCommitted: (filters: CommittedFilters | null) => void;
  reset: () => void;
}

const initial = () => ({
  properties: EMPTY_PROPERTIES,
  groups: [emptyStatGroup(1)],
  sort: DEFAULT_SORT,
  // Open, because with nothing submitted there are no results to make room for.
  filtersOpen: true,
  submitted: null,
  committed: null,
});

export const useItemSearchState = create<ItemSearchState>((set) => ({
  variant: null,
  ...initial(),
  chooseVariant: (variant) => set({ ...initial(), variant }),
  setProperties: (properties) => set({ properties }),
  setGroups: (groups) => set({ groups }),
  setSort: (sort) => set({ sort }),
  setFiltersOpen: (filtersOpen) => set({ filtersOpen }),
  setSubmitted: (submitted) => set({ submitted }),
  setCommitted: (committed) => set({ committed }),
  // The game stays: Reset clears the question, not which game it is asked of.
  reset: () => set(initial()),
}));
