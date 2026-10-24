/**
 * Notebook in the tree hierarchy.
 */
export interface Notebook {
  id: string;
  title: string;
  parentId: string;
  children: Notebook[];
  noteCount: number;
}

/**
 * A trashed notebook as listed in the trash (only the top of a trashed subtree).
 */
export interface TrashedNotebook {
  id: string;
  title: string;
  parentId: string;
  deletedTime: number;
  /** Notes trashed together with the notebook (restored with it), at any depth */
  noteCount: number;
  /** Sub-notebooks trashed together with the notebook, at any depth */
  notebookCount: number;
  /** Notes inside that were trashed on their own earlier: not restored, but deleted with it */
  earlierNoteCount: number;
}

/**
 * Note summary for lists and search results.
 */
export interface NoteListItem {
  id: string;
  title: string;
  updatedTime: number;
  createdTime: number;
  deletedTime?: number;
  preview: string;
  notebookId?: string;
  parentId?: string;
  isTodo?: boolean;
  todoCompleted?: boolean;
  /** To-do due date in Unix ms; 0 or missing when none is set */
  todoDue?: number;
}

/**
 * Full note including its Markdown body and metadata.
 */
export interface Note {
  id: string;
  title: string;
  body: string;
  /** Plain-text preview of the body, as in note lists (built by the server) */
  preview?: string;
  updatedTime: number;
  createdTime: number;
  deletedTime?: number;
  parentId: string;
  notebookId: string;
  notebookTitle: string;
  tags: Tag[];
  isTodo: boolean;
  todoCompleted: boolean;
  /** 1 = Markdown, 2 = HTML (see `MARKUP_HTML`) */
  markupLanguage?: number;
  /** To-do due date in Unix ms; 0 or missing when none is set */
  todoDue?: number;
}

/** `markupLanguage` of HTML notes, e.g. pages copied over from Joplin's Web Clipper */
export const MARKUP_HTML = 2;

/**
 * Tag model associated with notes.
 */
export interface Tag {
  id: string;
  title: string;
  noteCount: number;
}

/**
 * Search result with matched highlighted snippet fields.
 */
export interface SearchResult extends NoteListItem {
  matches?: {
    inTitle: boolean;
    inBody: boolean;
    snippet?: string;
    /** Lower-cased words and phrases the note matched (empty for filter-only queries) */
    terms?: string[];
  };
}

/**
 * Login response payload from API. The session itself is set as an httpOnly cookie.
 */
export interface LoginResponse {
  userId: string;
  user: UserInfo;
}

/**
 * An account. Administrators create accounts; there is no public registration.
 */
export interface UserInfo {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
  createdTime: number;
}

/**
 * An earlier state of a note's title and body, as listed in the version history.
 */
export interface NoteVersion {
  id: string;
  noteId: string;
  title: string;
  /** Plain-text start of the body */
  preview: string;
  /** When the note had this content (its updatedTime back then) */
  updatedTime: number;
  /** When the version was saved */
  createdTime: number;
}

/**
 * A version with its full body.
 */
export interface NoteVersionDetail {
  id: string;
  noteId: string;
  title: string;
  body: string;
  updatedTime: number;
  createdTime: number;
}

/**
 * A place where the account is signed in (one browser or API client).
 */
export interface SignInSession {
  id: string;
  createdTime: number;
  lastSeenTime: number;
  expiresTime: number;
  /** The browser's User-Agent at sign-in, shortened; empty for clients that send none */
  userAgent: string;
  /** The session making this request */
  current: boolean;
}
