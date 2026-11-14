-- v1.110 (the owner, 2026-11-14: «https://panel.hazem-office.tech/documents شوف دي عاوز تعملها» … «اعمل كله مع بعض»):
-- «المستندات» was a «coming soon» page. hazem.client_documents is every file a client sent, in one place, with whose it is:
--   message   a picture or a document (PDF, Word…) in one of his WhatsApp messages (hazem.turns), stored; not one of the
--             three below (the same wamid), which have a better name for it
--   receipt   a transfer's receipt (hazem.payment_receipts), for a booking or a service order
--   service   a document for a written service (hazem.service_files)
--   urf       a document for an Urfi case (hazem.urf_files)
-- kind is «image» or «document». Only where a file is (its id, and for a message the turn and the message's place in it),
-- never its content; the panel reads the file through the API, for whoever may see that client (PanelDocuments).

-- migrate:up
CREATE VIEW hazem.client_documents AS
SELECT 'message'::text AS source, t.id AS id, (m.ord - 1)::int AS "index", p.id AS person_id,
       -- what the file is, as its type says; the message's own type only when the file has none
       CASE WHEN coalesce(m.msg -> 'media' ->> 'mime_type', '') LIKE 'image/%'
              OR m.msg -> 'media' ->> 'mime_type' IS NULL AND m.msg ->> 'type' = 'image'
            THEN 'image' ELSE 'document' END AS kind,
       m.msg -> 'media' ->> 'mime_type' AS mime, m.msg -> 'media' ->> 'filename' AS name, t.created_at AS received_at,
       t.turn_id AS turn_id
  FROM hazem.turns t
  JOIN hazem.persons p ON p.wa_id = t.wa_id
  CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(t.messages) = 'array' THEN t.messages ELSE '[]'::jsonb END)
       WITH ORDINALITY AS m(msg, ord)
 WHERE m.msg ->> 'type' IN ('image', 'document')
   AND m.msg -> 'media' ->> 'status' = 'stored'
   AND m.msg -> 'media' ->> 'bucket' IS NOT NULL AND m.msg -> 'media' ->> 'key' IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM hazem.payment_receipts r WHERE r.wamid = m.msg ->> 'wamid')
   AND NOT EXISTS (SELECT 1 FROM hazem.service_files f WHERE f.wamid = m.msg ->> 'wamid')
   AND NOT EXISTS (SELECT 1 FROM hazem.urf_files f WHERE f.wamid = m.msg ->> 'wamid')
UNION ALL
SELECT 'receipt', r.id, 0, coalesce(a.person_id, o.person_id),
       CASE WHEN coalesce(r.mime, 'image/') LIKE 'image/%' THEN 'image' ELSE 'document' END, r.mime, NULL, r.received_at, NULL
  FROM hazem.payment_receipts r
  JOIN hazem.payments pay ON pay.id = r.payment_id
  LEFT JOIN hazem.appointments a ON a.id = pay.appointment_id
  LEFT JOIN hazem.service_orders o ON o.id = pay.service_order_id
UNION ALL
SELECT 'service', f.id, 0, o.person_id,
       CASE WHEN coalesce(f.mime, '') LIKE 'image/%' THEN 'image' ELSE 'document' END, f.mime, f.filename, f.received_at, NULL
  FROM hazem.service_files f
  JOIN hazem.service_orders o ON o.id = f.order_id
UNION ALL
SELECT 'urf', f.id, 0, c.person_id,
       CASE WHEN coalesce(f.mime, '') LIKE 'image/%' THEN 'image' ELSE 'document' END, f.mime, f.filename, f.received_at, NULL
  FROM hazem.urf_files f
  JOIN hazem.urf_cases c ON c.id = f.case_id;

GRANT SELECT ON hazem.client_documents TO office_api, office_readonly;

-- migrate:down
DROP VIEW hazem.client_documents;
