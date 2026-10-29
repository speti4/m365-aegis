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
  | { status: "signed-out"; info: SignedOutInfo }
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
  | { type: "reply"; reply: Reply };

/**
 * The sign-in card's state machine (task 040): `whoami` reads the state, `login` signs in (the device code shows
 * while it waits), `logout` signs out and the page reads the state again. A failed `login` goes back to where it
 * started, a failed `logout` keeps the account; the error itself is the page's toast.
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
