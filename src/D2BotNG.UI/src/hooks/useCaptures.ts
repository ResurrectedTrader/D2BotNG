/**
 * Reads of wire schema v2 captures.
 *
 * Split in two, because the two halves have very different weights:
 *
 *  - The **summaries** ride the event stream like everything else in the app. They are small, and
 *    the server sends a snapshot on connect plus one on every change, so the list is live for free.
 *  - A **capture** is fetched on demand. One carries every item's stat lists, which is far too much
 *    to push per profile per snapshot when a manager can be running hundreds. This is the app's
 *    only React Query *query*.
 *
 * Nothing polls. The server's `CaptureChanged` bumps a per-profile revision in the event store,
 * and the detail below refetches when the revision for the profile it is showing moves — so an
 * idle manager is silent, a busy one is current, and the heavy payload only ever moves for the
 * character actually on screen.
 */

import { useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { captureClient } from "@/lib/grpc-client";
import { useCaptureRevision } from "@/stores/event-store";
import { useDebounced } from "./useDebounced";

/**
 * How long to sit on a burst of changes before refetching the open character.
 *
 * Snapshots are not rare: gold and experience churn constantly, and each one is a change. Without
 * this, watching a running character would refetch its whole inventory several times a second to
 * show a gold counter tick.
 *
 * A ceiling on the refetch rate rather than a wait for quiet — see `useDebounced`. A running bot
 * reports faster than this interval, so "settle once nothing has changed for 750ms" never settled
 * at all and the open character stopped updating for as long as it kept running.
 */
const REFETCH_DEBOUNCE_MS = 750;

export const captureKeys = {
  character: (profile: string) => ["captures", "character", profile] as const,
};

/**
 * One character, whole: both wearers with every container, item and stat list. Skipped entirely
 * when no profile is given, so selecting a v1 character costs nothing.
 */
export function useCapturedCharacter(profile: string | null | undefined) {
  const queryClient = useQueryClient();

  /**
   * The revision AND whose it is, as one primitive.
   *
   * The throttle holds a single value across a character switch, so a bare revision let the newly
   * selected character be invalidated at the number belonging to the one before it. Carrying the
   * profile makes a switch a change of value rather than a value inherited — and it has to be a
   * primitive, since a fresh object every render would never compare equal and so never settle.
   *
   * Revision first, so the separator is unambiguous: a profile name may contain anything.
   */
  const settled = useDebounced(
    `${useCaptureRevision(profile)}@${profile ?? ""}`,
    REFETCH_DEBOUNCE_MS,
  );

  // Invalidating a stable key rather than putting the revision IN the key: one cache entry per
  // profile instead of one per revision, and React Query keeps showing the previous capture while
  // the next is in flight, so the gear does not blink on every change.
  //
  // Deliberately not depending on the live revision as well: that would run this on every bump and
  // undo the throttle the settled value exists to apply.
  useEffect(() => {
    const at = settled.indexOf("@");
    const revision = Number(settled.slice(0, at));
    // Revision 0 is "nothing reported yet", and a settled value from the previously open character
    // says nothing about this one.
    if (!profile || revision === 0 || settled.slice(at + 1) !== profile) return;
    void queryClient.invalidateQueries({
      queryKey: captureKeys.character(profile),
    });
  }, [queryClient, profile, settled]);

  return useQuery({
    queryKey: captureKeys.character(profile ?? ""),
    queryFn: () => captureClient.getCharacter({ profile: profile! }),
    enabled: !!profile,
    // The stream says when this is stale; nothing else should second-guess it.
    refetchOnWindowFocus: false,
    staleTime: Infinity,
  });
}
