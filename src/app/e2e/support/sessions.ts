/**
 * The extra personas a spec needs beside the admin its `page` already is - an anonymous visitor and
 * any signed-in account - each in its own browser context, never signed in through the form.
 *
 * Contexts made with `browser.newContext()` inherit the config's `use`, the admin's storageState
 * included, so every one here passes an EMPTY storageState explicitly.
 */
import { Browser, BrowserContext, expect, request as playwrightRequest } from '@playwright/test';

const noSession = { cookies: [], origins: [] };

let addressCounter = 0;

/**
 * A fake client address, unique per call, to send as X-Forwarded-For on registration.
 *
 * Registration is capped at 3 per 5 minutes per client, and the limiter partitions on the LAST
 * X-Forwarded-For segment (RateLimitPolicies.PartitionKey). Every run of these specs registers
 * accounts, so without this a second run - or a CI retry - would get 429. The cap is a courtesy limit
 * deliberately NOT under test here; the integration tests own it.
 */
export function uniqueClientAddress(): string {
  addressCounter += 1;
  const seed = (Date.now() + process.pid * 7919 + addressCounter * 104729) % 16_777_216;
  return `10.${(seed >> 16) & 255}.${(seed >> 8) & 255}.${seed & 255}`;
}

/** A signed-out visitor whose requests, the SPA's own register call included, carry a unique client. */
export async function anonymousContext(browser: Browser): Promise<BrowserContext> {
  return browser.newContext({
    storageState: noSession,
    extraHTTPHeaders: { 'X-Forwarded-For': uniqueClientAddress() },
  });
}

/** A context signed in as `credentials`, through the API rather than the login form. */
export async function signedInContext(
  browser: Browser,
  credentials: { email: string; password: string },
): Promise<BrowserContext> {
  const context = await browser.newContext({ storageState: noSession });
  const response = await context.request.post('/api/auth/login', { data: credentials });
  expect(response.ok(), `sign-in as ${credentials.email}`).toBeTruthy();
  return context;
}

/**
 * Registers an account from an invitation code through the API, in a throwaway request context so
 * the session it opens never replaces the caller's.
 */
export async function registerViaApi(
  baseURL: string,
  account: { email: string; password: string; code: string },
): Promise<void> {
  const context = await playwrightRequest.newContext({
    baseURL,
    extraHTTPHeaders: { 'X-Forwarded-For': uniqueClientAddress() },
  });
  try {
    const response = await context.post('/api/auth/register', {
      data: { email: account.email, password: account.password, memberCode: account.code },
    });
    expect(response.ok(), `register ${account.email}: ${await response.text()}`).toBeTruthy();
  } finally {
    await context.dispose();
  }
}
