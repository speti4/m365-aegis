/** When a snapshot was taken, as the cards write it: the UTC minute, or the core's text when it is not a time. */
export function describeTaken(taken: string): string {
  const time = Date.parse(taken);
  return Number.isNaN(time) ? taken : `${new Date(time).toISOString().slice(0, 16).replace("T", " ")} UTC`;
}
