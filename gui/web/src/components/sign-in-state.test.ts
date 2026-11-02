import { describe, expect, it } from "vitest";
import type { DeviceCodePrompt, Reply, SignedOutInfo, SignInInfo } from "../bridge/protocol";
import { describeElapsed } from "./mailbox-access-card";
import { canSignIn, type SignInState, signInReducer, signInStatus } from "./sign-in-state";

const alice: SignInInfo = { profile: "work", account: "alice@contoso.com", tokens: [] };
const signedOut: SignedOutInfo = {
  profile: "work",
  account: "alice@contoso.com",
  message: "no silent sign-in for alice@contoso.com",
  hint: "run `m365-aegis login`",
};
const prompt: DeviceCodePrompt = {
  verificationUri: "https://login.microsoftonline.com/common/oauth2/deviceauth",
  userCode: "ABCD1234",
  message: "To sign in, open the page and enter the code ABCD1234.",
  expiresOn: "2026-10-29T14:15:00+00:00",
};
const error: Reply = {
  type: "error",
  data: { code: "auth", message: "the sign-in was cancelled in the account picker" },
};

const run = (state: SignInState, ...events: Parameters<typeof signInReducer>[1][]) =>
  events.reduce(signInReducer, state);

describe("the sign-in card's state", () => {
  it("signs in from signed out: waiting, the device code when the host sends one, then the account", () => {
    const start: SignInState = { status: "signed-out", info: signedOut };
    const waiting = run(start, { type: "login" });
    expect(waiting).toEqual({ status: "signing-in", before: signedOut });

    const withCode = run(waiting, { type: "device-code", prompt });
    expect(withCode).toEqual({ status: "signing-in", before: signedOut, deviceCode: prompt });

    expect(run(withCode, { type: "reply", reply: { type: "signed-in", data: alice } })).toEqual({
      status: "signed-in",
      info: alice,
    });
  });

  it("goes back to where it started when the sign-in fails", () => {
    expect(run({ status: "signed-out", info: signedOut }, { type: "login" }, { type: "reply", reply: error })).toEqual({
      status: "signed-out",
      info: signedOut,
    });
    expect(run({ status: "unknown" }, { type: "login" }, { type: "reply", reply: error })).toEqual({
      status: "unknown",
    });
  });

  it("ignores a device code nobody waits for, and a second login while one runs", () => {
    const signedIn: SignInState = { status: "signed-in", info: alice };
    expect(run(signedIn, { type: "device-code", prompt })).toBe(signedIn);
    expect(run(signedIn, { type: "login" })).toBe(signedIn);
    const waiting = run({ status: "signed-out", info: signedOut }, { type: "login" });
    expect(run(waiting, { type: "login" })).toBe(waiting);
  });

  it("signs out: the account stays shown until the host answers, then the page reads the state again", () => {
    const signingOut = run({ status: "signed-in", info: alice }, { type: "logout" });
    expect(signingOut).toEqual({ status: "signing-out", info: alice });
    expect(
      run(signingOut, {
        type: "reply",
        reply: { type: "logged-out", data: { profile: "work", account: "alice@contoso.com", removedAccounts: 1 } },
      }),
    ).toEqual({ status: "checking" });
    expect(run(signingOut, { type: "reply", reply: error })).toEqual({ status: "signed-in", info: alice });
  });

  it("reads whoami's answers, and an error as an unknown state", () => {
    expect(run({ status: "unknown" }, { type: "check" })).toEqual({ status: "checking" });
    expect(run({ status: "checking" }, { type: "reply", reply: { type: "signed-out", data: signedOut } })).toEqual({
      status: "signed-out",
      info: signedOut,
    });
    expect(run({ status: "checking" }, { type: "reply", reply: error })).toEqual({ status: "unknown" });
  });

  it("offers to sign in only when signed out with a profile", () => {
    expect(canSignIn({ status: "signed-out", info: signedOut })).toBe(true);
    expect(
      canSignIn({ status: "signed-out", info: { message: "no tenant profile", hint: "run `m365-aegis login …`" } }),
    ).toBe(false);
    expect(canSignIn({ status: "signed-in", info: alice })).toBe(false);
    expect(canSignIn({ status: "signing-in", before: signedOut })).toBe(false);
  });
});

describe("the mailbox permissions card's wording", () => {
  it("writes the answer's time in milliseconds under a second, in seconds above", () => {
    expect(describeElapsed(412.4)).toBe("412 ms");
    expect(describeElapsed(2412)).toBe("2.4 s");
  });
});

describe("the sign-in card's state at its edges (task 070)", () => {
  it("does not sign out what is not signed in, and keeps the state on a reply of another kind", () => {
    const checking: SignInState = { status: "checking" };
    expect(signInReducer(checking, { type: "logout" })).toBe(checking);
    const pong: Reply = { type: "pong", data: { product: "m365-aegis", version: "0.1.0" } };
    expect(signInReducer(checking, { type: "reply", reply: pong })).toBe(checking);
  });
});

describe("what the sign-in card's status region says (task 121)", () => {
  it("names each state in a few words", () => {
    const info = alice;
    expect(signInStatus({ status: "checking" })).toBe("Checking the sign-in…");
    expect(signInStatus({ status: "signed-in", info })).toBe("Signed in as alice@contoso.com");
    expect(signInStatus({ status: "signed-out", info: { message: "m" } })).toBe("Signed out");
    expect(signInStatus({ status: "signing-in", before: null })).toBe(
      "Waiting for the sign-in: choose the account in the Windows sign-in window",
    );
    expect(signInStatus({ status: "signing-in", before: null, deviceCode: prompt })).toBe(
      "Waiting for the sign-in: enter the code in a browser",
    );
    expect(signInStatus({ status: "signing-out", info })).toBe("Signing out…");
    expect(signInStatus({ status: "unknown" })).toBe("The sign-in state could not be read");
    expect(signInStatus({ status: "signed-out", info: { message: "m" }, canceled: true })).toBe(
      "Sign-in canceled; signed out",
    );
  });

  it("stops waiting for a sign-in on Cancel, back to where it started (task 126)", () => {
    const waiting = run({ status: "signed-out", info: signedOut }, { type: "login" }, { type: "device-code", prompt });
    expect(run(waiting, { type: "cancel" })).toEqual({ status: "signed-out", info: signedOut, canceled: true });
    expect(run({ status: "unknown" }, { type: "login" }, { type: "cancel" })).toEqual({ status: "unknown" });
    // Nothing to cancel: the state stays.
    const signedIn: SignInState = { status: "signed-in", info: alice };
    expect(run(signedIn, { type: "cancel" })).toBe(signedIn);
  });
});
