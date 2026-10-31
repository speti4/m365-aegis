import { mockAccess } from "./mock-access";
import { type MockForwarding, MockWrites, mockForwarding } from "./mock-writes";
import {
  type BridgeError,
  type BridgeMessage,
  type GroupMembers,
  type MailboxAccess,
  type MailboxInfo,
  MessageType,
  PROTOCOL_VERSION,
  type SignInInfo,
  type UserAccess,
} from "./protocol";
import type { Transport } from "./transport";

/** The browser mock's signed-in account: made up, as the samples under tests/contracts/bridge. */
const mockSignIn: SignInInfo = {
  profile: "work",
  account: "alice@contoso.com",
  displayName: "Alice Example",
  tenantId: "00000000-0000-0000-0000-000000000002",
  objectId: "00000000-0000-0000-0000-000000000001",
  tokens: [
    {
      resource: "graph",
      expiresOn: "2026-10-29T15:00:00+01:00",
      scopes: ["User.Read", "Group.Read.All"],
      origin: "broker",
    },
    { resource: "exchange", error: "the consent for Exchange Online is missing (a mock)", scopes: [] },
  ],
};

/** The browser mock's one group. */
const mockFinance: GroupMembers = {
  group: {
    id: "00000000-0000-0000-0000-000000000031",
    displayName: "Finance",
    kind: "microsoft365",
    mail: "finance@contoso.com",
    synced: false,
  },
  members: [
    {
      type: "user",
      displayName: "Alice Example",
      userPrincipalName: "alice@contoso.com",
      mail: "alice@contoso.com",
      id: "00000000-0000-0000-0000-000000000001",
    },
    {
      type: "user",
      displayName: "Bob Example",
      userPrincipalName: "bob@contoso.com",
      mail: "bob@contoso.com",
      id: "00000000-0000-0000-0000-000000000003",
    },
    {
      type: "user",
      displayName: "Carol Example",
      userPrincipalName: "carol@contoso.com",
      id: "00000000-0000-0000-0000-000000000004",
    },
    {
      type: "group",
      displayName: "Finance EU",
      mail: "finance-eu@contoso.com",
      id: "00000000-0000-0000-0000-000000000032",
    },
  ],
};

const mailbox = (name: string, address: string, type: string, id: string): MailboxInfo => ({
  displayName: name,
  primarySmtpAddress: address,
  type,
  userPrincipalName: address,
  objectId: `00000000-0000-0000-0000-0000000000${id}`,
});

/** The browser mock's mailboxes: the made-up ones of the fake Exchange Online module (tests/sidecar/README.md). */
const mockMailboxes: MailboxInfo[] = [
  mailbox("Alice Example", "alice@contoso.com", "UserMailbox", "11"),
  mailbox("Bob Example", "bob@contoso.com", "UserMailbox", "12"),
  mailbox("Finance", "finance@contoso.com", "SharedMailbox", "15"),
  mailbox("Reception", "reception@contoso.com", "SharedMailbox", "16"),
];

const [alice, bob, finance, reception] = mockMailboxes;

/**
 * The mock's grants over the layers, as the host reads them (task 063): the mailbox and its Send on Behalf delegates
 * by name from the Admin API, FullAccess and Send As from the session; bob's mailbox the Admin API refuses, as the
 * recorded answers do, so the session reads it alone.
 */
const mockHolders: MailboxAccess[] = [
  {
    mailbox: finance,
    holders: [
      { right: "full-access", holder: "alice@contoso.com", address: "alice@contoso.com", layer: "pwsh" },
      { right: "send-as", holder: "alice@contoso.com", address: "alice@contoso.com", layer: "pwsh" },
      { right: "send-on-behalf", holder: "Bob Example", address: "bob@contoso.com", layer: "admin-api" },
    ],
    mailboxLayer: "admin-api",
  },
  {
    mailbox: reception,
    holders: [
      { right: "full-access", holder: "alice@contoso.com", address: "alice@contoso.com", layer: "pwsh" },
      { right: "full-access", holder: "bob@contoso.com", address: "bob@contoso.com", layer: "pwsh" },
      { right: "send-as", holder: "bob@contoso.com", address: "bob@contoso.com", layer: "pwsh" },
    ],
    mailboxLayer: "admin-api",
  },
  { mailbox: alice, holders: [], mailboxLayer: "admin-api" },
  { mailbox: bob, holders: [], mailboxLayer: "pwsh" },
];

/** The mock's holders as one mock starts with them: its writes change its own copy (task 091). */
const freshHolders = (): MailboxAccess[] =>
  mockHolders.map((access) => ({ ...access, holders: access.holders.map((holder) => ({ ...holder })) }));

/** The reverse lookup of a user over the mock's holders; bob's answer is partial, to show the page's warning. */
function mockGrants(holders: readonly MailboxAccess[], user: MailboxInfo): UserAccess {
  const grants = holders.flatMap(({ mailbox, holders }) =>
    holders
      .filter((holder) => holder.address?.toLowerCase() === user.primarySmtpAddress)
      .map((holder) => ({ right: holder.right, mailbox })),
  );
  return {
    user: user.primarySmtpAddress,
    displayName: user.displayName,
    grants,
    mailboxesScanned: mockMailboxes.length,
    failed: user === bob ? ["the FullAccess list of 'Archive' could not be read (a mock)"] : [],
  };
}

function find(name: string): MailboxInfo | undefined {
  const wanted = name.trim().toLowerCase();
  return mockMailboxes.find((candidate) =>
    [candidate.displayName, candidate.primarySmtpAddress, candidate.objectId].some(
      (value) => value?.toLowerCase() === wanted,
    ),
  );
}

const notFound = (what: string, name: string, hint: string): { type: string; data: BridgeError } => ({
  type: MessageType.error,
  data: { code: "not-found", message: `${what} '${name}' not found`, hint },
});

export interface MockOptions {
  /** How long the mock takes to answer, so the page's waiting state shows. */
  delayMs?: number;
  /** Whether the mock starts signed in (the default) or signed out. */
  signedIn?: boolean;
  /** Whether the profile has a snapshot for the access card (the default) or none (task 102). */
  snapshot?: boolean;
}

/**
 * The browser mock: no host, so the requests are answered here with made-up data after `delayMs`, so the page's
 * waiting state shows: a signed-in account (or a signed-out one, which `login` signs in: through a device code when
 * asked, with the `device-code` notification first), one group, `Finance`, by name, address or id, and the
 * mailboxes of the fake Exchange Online module, and the mailbox writes planned over them and applied only with the
 * confirmation (`MockWrites`, task 082); an apply changes this mock's own holders and forwarding, so a lookup after it
 * shows the change (task 091), and dave's access from the snapshot (`mock-access.ts`, task 102), signed in or not, or the
 * answer without a snapshot. Anything else is not found, an unknown type an error.
 */
export class MockTransport implements Transport {
  private handlers: Array<(message: BridgeMessage) => void> = [];
  private readonly delayMs: number;
  private signedIn: boolean;
  private readonly snapshot: boolean;
  /** The page of the device-code sign-in in flight, which `open-sign-in-page` "opens"; null when none waits. */
  private signInPage: string | null = null;
  /** The mailboxes' holders and forwarding, which an apply changes. */
  private readonly holders = freshHolders();
  private readonly forwarding: MockForwarding = mockForwarding();
  /** The plans waiting for their confirmation; a sign-in or a sign-out drops them, as the host does. */
  private writes = this.newWrites();

  constructor({ delayMs = 0, signedIn = true, snapshot = true }: MockOptions = {}) {
    this.delayMs = delayMs;
    this.signedIn = signedIn;
    this.snapshot = snapshot;
  }

  send(message: BridgeMessage): void {
    if (message.id === undefined) {
      return;
    }
    const deviceCode =
      message.type === MessageType.login && (message.data as { deviceCode?: unknown } | undefined)?.deviceCode === true;
    if (deviceCode) {
      this.signInPage = "https://login.microsoftonline.com/common/oauth2/deviceauth";
      this.deliver(
        {
          aegis: PROTOCOL_VERSION,
          type: MessageType.deviceCode,
          data: {
            verificationUri: this.signInPage,
            userCode: "MOCK1234",
            message: "To sign in, open the page and enter the code MOCK1234 (a mock: nothing to do).",
            expiresOn: new Date(Date.now() + 15 * 60_000).toISOString(),
          },
        },
        this.delayMs,
      );
    }
    const reply: BridgeMessage = { aegis: PROTOCOL_VERSION, id: message.id, ...this.answer(message) };
    this.deliver(reply, deviceCode ? this.delayMs * 10 : this.delayMs, () => {
      if (message.type === MessageType.login) {
        this.signInPage = null;
      }
    });
  }

  onMessage(handler: (message: BridgeMessage) => void): () => void {
    this.handlers.push(handler);
    return () => {
      this.handlers = this.handlers.filter((registered) => registered !== handler);
    };
  }

  private newWrites(): MockWrites {
    return new MockWrites(find, this.holders, this.forwarding);
  }

  private deliver(message: BridgeMessage, afterMs: number, before?: () => void): void {
    setTimeout(() => {
      before?.();
      for (const handler of this.handlers) {
        handler(message);
      }
    }, afterMs);
  }

  private answer(message: BridgeMessage): { type: string; data: unknown } {
    const payload = (message.data ?? {}) as Record<string, unknown>;
    const text = (name: string) => (typeof payload[name] === "string" ? (payload[name] as string).trim() : "");
    switch (message.type) {
      case MessageType.ping:
        return { type: MessageType.pong, data: { product: "m365-aegis", version: "dev (browser mock)" } };
      case MessageType.whoami:
        return this.signedIn
          ? { type: MessageType.signedIn, data: mockSignIn }
          : {
              type: MessageType.signedOut,
              data: {
                profile: "work",
                account: "alice@contoso.com",
                message: "no silent sign-in for alice@contoso.com (a mock)",
                hint: "choose Sign in on the sign-in card to sign in again",
              },
            };
      case MessageType.login:
        this.signedIn = true;
        this.writes = this.newWrites();
        return { type: MessageType.signedIn, data: mockSignIn };
      case MessageType.openSignInPage:
        // A plain browser has no host to hand the page to: the mock only answers as the host would.
        return this.signInPage
          ? { type: MessageType.signInPageOpened, data: { verificationUri: this.signInPage } }
          : {
              type: MessageType.error,
              data: {
                code: "usage",
                message: "no device code sign-in is waiting for its browser step",
                hint: "choose Sign in with a code first; the card shows the page and the code",
              },
            };
      case MessageType.logout:
        this.signedIn = false;
        this.writes = this.newWrites();
        return {
          type: MessageType.loggedOut,
          data: { profile: "work", account: "alice@contoso.com", removedAccounts: 1 },
        };
      case MessageType.groupMembers: {
        const name = text("group");
        if (name === "") {
          return { type: MessageType.error, data: { code: "usage", message: "the group is missing" } };
        }
        const { id, displayName, mail } = mockFinance.group;
        return [id, displayName, mail].some((value) => value?.toLowerCase() === name.toLowerCase())
          ? { type: MessageType.members, data: mockFinance }
          : {
              type: MessageType.error,
              data: {
                code: "not-found",
                message: `no group named '${name}'`,
                hint: "give the exact display name, the address, the object id, or a word of the name",
              },
            };
      }
      case MessageType.mailboxList: {
        const shared = payload.shared === true;
        return {
          type: MessageType.mailboxes,
          data: {
            mailboxes: mockMailboxes.filter((candidate) => !shared || candidate.type === "SharedMailbox"),
            sharedOnly: shared,
          },
        };
      }
      case MessageType.mailboxAccess: {
        const mailboxName = text("mailbox");
        const userName = text("user");
        if ((mailboxName === "") === (userName === "")) {
          return {
            type: MessageType.error,
            data: { code: "usage", message: "give a mailbox or a user, not both and not neither" },
          };
        }
        if (userName !== "") {
          const user = find(userName);
          return user
            ? { type: MessageType.userGrants, data: mockGrants(this.holders, user) }
            : notFound("user", userName, "check the name or the address and try again");
        }
        const found = find(mailboxName);
        const access = this.holders.find((candidate) => candidate.mailbox === found);
        // A copy: the page keeps the answer it showed while a later apply changes the mock's holders.
        return access
          ? { type: MessageType.mailboxHolders, data: { ...access, holders: [...access.holders] } }
          : notFound("mailbox", mailboxName, "check the name or the address and try again");
      }
      case MessageType.access:
        return mockAccess(text("user"), this.snapshot);
      case MessageType.mailboxAccessGrant:
      case MessageType.mailboxAccessRevoke:
      case MessageType.mailboxForwardingSet:
      case MessageType.mailboxForwardingClear:
        return this.writes.plan(message.type, payload);
      case MessageType.applyPlan:
        return this.writes.apply(payload);
      default:
        return { type: MessageType.error, data: { code: "no-host", message: "no host in the browser" } };
    }
  }
}
