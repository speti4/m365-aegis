import {
  type DeviceCodePrompt,
  MessageType,
  type Reply,
  type SignedOutInfo,
  type SignInInfo,
} from "../bridge/protocol";

/** What the page knows about the sign-in. */
export type SignInState =
  | { status: "checking" }
  | { status: "signed-in"; info: SignInInfo }
  /** `canceled` when the page stopped waiting for a sign-in (task 126). */
  | { status: "signed-out"; info: SignedOutInfo; canceled?: boolean }
  /** A `login` in flight: the account picker over the window, or the device code once the host sent one. */
  | { status: "signing-in"; before: SignedOutInfo | null; deviceCode?: DeviceCodePrompt }
  | { status: "signing-out"; info: SignInInfo }
  | { status: "unknown" };

/** What happens to the sign-in: the page's own steps and the host's answers. */
export type SignInEvent =
  | { type: "check" }
  | { type: "login" }
  | { type: "device-code"; prompt: DeviceCodePrompt }
  | { type: "logout" }
  /** The colleague stops waiting for a sign-in in flight (task 126). */
  | { type: "cancel" }
  | { type: "reply"; reply: Reply };

/**
 * The sign-in card's state machine (task 040): `whoami` reads the state, `login` signs in (the device code shows
 * while it waits), `logout` signs out and the page reads the state again. A failed `login` goes back to where it
 * started, a failed `logout` keeps the account; the error itself is the page's toast. `cancel` drops a sign-in in
 * flight back to where it started (task 126): the bridge has no message to stop the host's sign-in, so the host's
 * account picker or device code runs on until it ends or times out, and the page ignores its late reply (`App` drops
 * the reply of a sign-in it no longer waits for).
 */
export function signInReducer(state: SignInState, event: SignInEvent): SignInState {
  switch (event.type) {
    case "check":
      return { status: "checking" };
    case "login":
      return state.status === "signed-out" || state.status === "unknown"
        ? { status: "signing-in", before: state.status === "signed-out" ? state.info : null }
        : state;
    case "device-code":
      return state.status === "signing-in" ? { ...state, deviceCode: event.prompt } : state;
    case "logout":
      return state.status === "signed-in" ? { status: "signing-out", info: state.info } : state;
    case "cancel":
      if (state.status !== "signing-in") {
        return state;
      }
      return state.before ? { status: "signed-out", info: state.before, canceled: true } : { status: "unknown" };
    case "reply":
      return afterReply(state, event.reply);
  }
}

function afterReply(state: SignInState, reply: Reply): SignInState {
  switch (reply.type) {
    case MessageType.signedIn:
      return { status: "signed-in", info: reply.data };
    case MessageType.signedOut:
      return { status: "signed-out", info: reply.data };
    case MessageType.loggedOut:
      // Signed out: the page reads the state again, which says what is left (the profile, the hint).
      return { status: "checking" };
    case MessageType.error:
      if (state.status === "signing-in") {
        return state.before ? { status: "signed-out", info: state.before } : { status: "unknown" };
      }
      if (state.status === "signing-out") {
        return { status: "signed-in", info: state.info };
      }
      return { status: "unknown" };
    default:
      return state;
  }
}

/** Whether the card offers to sign in: signed out with a profile (without one, the command line writes it first). */
export function canSignIn(state: SignInState): boolean {
  return state.status === "signed-out" && state.info.profile !== undefined;
}

/**
 * Why the window has no Exchange Online token though it is signed in (task 124): the error of the whoami reply's
 * Exchange Online row, which the mailbox cards cannot work without; null when the row has a token, or when the window
 * is not signed in.
 */
export function exchangeError(state: SignInState): string | null {
  if (state.status !== "signed-in") {
    return null;
  }
  return state.info.tokens.find((token) => token.resource === "exchange")?.error ?? null;
}

/** The state in a few words, as the badge on the sign-in card and the top bar write it; null while it is read. */
export function statusLabel(state: SignInState): string | null {
  switch (state.status) {
    case "signed-in":
      return exchangeError(state) === null ? "signed in" : "signed in · Exchange unavailable";
    case "signed-out":
      return "signed out";
    case "signing-in":
      return "signing in…";
    case "signing-out":
      return "signing out…";
    case "unknown":
      return "sign-in unknown";
    default:
      return null;
  }
}

/** What the sign-in card's status region says for a state (task 121): each change of the sign-in, in a few words. */
export function signInStatus(state: SignInState): string {
  switch (state.status) {
    case "checking":
      return "Checking the sign-in…";
    case "signed-in":
      return exchangeError(state) === null
        ? `Signed in as ${state.info.account}`
        : `Signed in as ${state.info.account}; Exchange Online is unavailable`;
    case "signed-out":
      return state.canceled ? "Sign-in canceled; signed out" : "Signed out";
    case "signing-in":
      return state.deviceCode
        ? "Waiting for the sign-in: enter the code in a browser"
        : "Waiting for the sign-in: choose the account in the Windows sign-in window";
    case "signing-out":
      return "Signing out…";
    case "unknown":
      return "The sign-in state could not be read";
  }
}
