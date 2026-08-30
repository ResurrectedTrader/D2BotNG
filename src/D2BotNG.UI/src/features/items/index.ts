/**
 * What the rest of the app renders items with.
 *
 * The feature's outward surface, not a listing of it: everything else here — the clipboard
 * actions, the PNG capture, the card — is reached from inside the feature by relative import, so
 * adding it to the barrel would only advertise internals as though they were the contract.
 */

export { ItemTooltip, ItemTooltipContent } from "./ItemTooltip";

export { TooltipLine, useTooltipTextStyle } from "./TooltipText";

export { CtrlBreakdownHint } from "./CtrlBreakdownHint";

export { useItemContextMenu } from "./useItemContextMenu";

export { isEthereal } from "./item-utils";
export type { RenderableItem } from "./item-utils";
