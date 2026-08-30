/**
 * Item search over wire schema v2 captures.
 *
 * A query rather than a mutation, and — like the other capture reads — one of the few in the app.
 * Unlike them it is NOT polled: a search is something the user asked for at a moment, and
 * re-running it underneath them would reshuffle the page they are reading. Refetching is theirs.
 *
 * The request is the cache key. That is safe because it is built fresh from the filter state and
 * carries no functions, and it means an unchanged search is free while any edit is a new entry.
 * The OFFSET is deliberately not part of it: paging is the hook's business, not the caller's, and
 * an offset in the key would make every page its own cache entry and every scroll a new search.
 */

import { useMemo } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { ConnectError, Code } from "@connectrpc/connect";
import { toJson } from "@bufbuild/protobuf";
import {
  type SearchItemsRequest,
  type SearchItemsResponse,
  SearchItemsRequestSchema,
} from "@/generated/captures_pb";
import { captureClient } from "@/lib/grpc-client";

/**
 * The store rejects a request it cannot answer literally instead of repairing it, so a rejection
 * is a statement about the filter rather than a failure. Surfaced as a message the user can act
 * on rather than a toast that scrolls away.
 */
export function searchRejectionMessage(error: unknown): string | null {
  if (error instanceof ConnectError && error.code === Code.InvalidArgument) {
    return error.rawMessage;
  }
  return null;
}

/**
 * One search, fetched a page at a time as the reader scrolls.
 *
 * Infinite rather than numbered pages because a result list is read by scanning it: the reader is
 * comparing items, not visiting page three. The store already answers offset/limit and reports the
 * full `total` with every page, so this is only a matter of asking for the next slice when the end
 * of the list comes into view.
 *
 * `getNextPageParam` counts what has actually arrived rather than multiplying by the page size:
 * the last page is short, and the count is what the next offset means.
 */
export function useItemSearch(request: SearchItemsRequest | null) {
  // Serialised once per request rather than on every render: the whole filter set, item lists and
  // stat conditions included, walks the schema to become the key — and the page re-renders whenever
  // any profile reports a capture, not only when the search changes.
  const key = useMemo(
    () => (request ? toJson(SearchItemsRequestSchema, request) : null),
    [request],
  );

  return useInfiniteQuery({
    queryKey: ["captures", "search", key],
    queryFn: ({ pageParam }) =>
      captureClient.searchItems({ ...request!, offset: pageParam }),
    initialPageParam: 0,
    getNextPageParam: (last: SearchItemsResponse, pages) => {
      const loaded = pages.reduce((n, page) => n + page.results.length, 0);
      // A page that came back empty ends it whatever the total says, so a total that disagrees
      // with the rows cannot spin this forever.
      return last.results.length > 0 && loaded < last.total
        ? loaded
        : undefined;
    },
    enabled: !!request,
    // A capture changes only when a profile reports, and the user drives when to look again.
    refetchOnWindowFocus: false,
    staleTime: Infinity,
    // A rejected filter is not going to become valid by being retried. Anything else gets the
    // default three attempts and then reports — a bare `true` here means retry FOREVER, which
    // leaves an unreachable backend as a Search button stuck on "Searching…" and no error ever
    // shown, since React Query holds the error back while attempts are still in flight.
    retry: (failures, error) =>
      searchRejectionMessage(error) === null && failures < 3,
  });
}
