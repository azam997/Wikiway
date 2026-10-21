namespace Wikiway.Core.Models;

// The slow half of an item card: where to get it (ACQUISITION) and what it's
// used for (USAGE). Built from whole-sheet scans when the card is opened, not
// for every search result. Each is null when the sheets list nothing.
public sealed record ItemDetails(ItemAcquisition? Acquisition, ItemUsage? Usage);
