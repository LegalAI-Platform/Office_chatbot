// v1.110 (the owner: «https://panel.hazem-office.tech/documents شوف دي عاوز تعملها» … «اعمل كله مع بعض»): «المستندات»,
// every file a client sent, by client. The search goes in the body (POST), as the search bar's: never in the address.
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "../../shared/api/client";

export type DocumentKind = "image" | "document";
export type DocumentSource = "message" | "receipt" | "service" | "urf";

export interface DocumentFile {
  source: DocumentSource;
  id: string;
  /** a message's place in its turn (a message's file); 0 otherwise */
  index: number;
  kind: DocumentKind;
  mime: string | null;
  name: string | null;
  received_at: string;
  /** the task opened from the same messages, when there is one */
  task_id: string | null;
}

export interface DocumentClient {
  id: string;
  name: string | null;
  phone_last3: string | null;
  /** how many files he has (the newest are in `files`) */
  count: number;
  latest_at: string;
  files: DocumentFile[];
}

export interface DocumentPage {
  clients: DocumentClient[];
  total: number;
  page: number;
  page_size: number;
}

export type KindFilter = "all" | DocumentKind;
export type SourceFilter = "all" | DocumentSource;

export const documentsKey = ["documents"] as const;

export function useDocuments(q: string, kind: KindFilter, source: SourceFilter, page: number) {
  return useQuery({
    queryKey: [...documentsKey, q, kind, source, page],
    queryFn: () => api<DocumentPage>("POST", "/documents", {
      q: q || null, kind: kind === "all" ? null : kind, source: source === "all" ? null : source, page,
    }),
    placeholderData: keepPreviousData,
  });
}

/** The file through the API: shown in the browser when it is safe to (a picture, a PDF), else downloaded; recorded. */
export const documentUrl = (f: DocumentFile): string => `/api/panel/documents/${f.source}/${f.id}/${f.index}`;
