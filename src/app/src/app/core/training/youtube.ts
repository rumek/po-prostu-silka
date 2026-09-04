/**
 * The one place that composes YouTube URLs from a stored video id, so the list's thumbnail, the
 * form's canonical link and the detail screen's player cannot drift apart.
 *
 * NOTE WHAT IS NOT HERE: parsing. The server owns that (Domain/Training/YouTubeVideoId.cs) and
 * stores only the id, precisely so a second implementation cannot disagree with the first. These
 * helpers go id → URL, never the other way.
 */

/** Matches the server's pattern. An id is 11 characters of URL-safe base64. */
const ID_PATTERN = /^[A-Za-z0-9_-]{11}$/;

/**
 * Whether a value is a well-formed video id.
 *
 * The API already guarantees this, so it is belt-and-braces everywhere except one place: the detail
 * screen passes the id through DomSanitizer.bypassSecurityTrustResourceUrl, and that is the single
 * call in this app where trusting an unchecked value would turn a future upstream change into an
 * injection. Cheap here, load-bearing there.
 */
export function isVideoId(value: string | null | undefined): value is string {
  return typeof value === 'string' && ID_PATTERN.test(value);
}

/**
 * The 320x180 thumbnail. `mqdefault` exists for every video, unlike `maxresdefault`, which 404s on
 * anything never uploaded in HD — a broken image on a list is worse than a smaller one.
 */
export function thumbnailUrl(videoId: string): string {
  return `https://img.youtube.com/vi/${videoId}/mqdefault.jpg`;
}

/** The canonical watch URL — what the edit form shows for an exercise that already has a video. */
export function watchUrl(videoId: string): string {
  return `https://www.youtube.com/watch?v=${videoId}`;
}

/**
 * The player URL for the detail screen's iframe.
 *
 * youtube-nocookie.com rather than youtube.com: it is YouTube's own privacy-preserving host, and
 * this is an internal admin tool that has no business setting tracking cookies on the club's staff.
 */
export function embedUrl(videoId: string): string {
  return `https://www.youtube-nocookie.com/embed/${videoId}`;
}
