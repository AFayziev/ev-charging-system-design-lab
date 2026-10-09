import { type PlatformRole } from "./PlatformRole";

export interface BrowserSession {
  signedIn: boolean;
  user: BrowserSessionUser | null;
  antiforgeryToken: string | null;
}

export interface BrowserSessionUser {
  name: string | null;
  username: string | null;
  email: string | null;
  roles: PlatformRole[];
}
