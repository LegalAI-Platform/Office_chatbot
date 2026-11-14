-- migrate:up
-- DECISIONS v1.113 (the owner, 2026-11-14, of a client's message that showed 8 times in Chatwoot: «هي ظاهره في الشات ووت ان
-- الرساله بتاعتها جت اكتر من مره» … «أيوه، صلّحه»).
--   That client has a WhatsApp username, so the contact key is a BSUID («EG.…», 0039, TASK 15). 0039 let every hazem table that
--   holds a contact key take one, but 0072 made hazem.chatwoot_inbound later with the old digits-only check: the early post
--   could never be recorded, so it went to Chatwoot again every round (about 3 s) until the turn was stored, and once more with
--   the turn. Meta sent the message once and Hazem answered once; only Chatwoot had 8 copies.
--   Now hazem.chatwoot_inbound takes every contact key, as the tables 0039 changed. The code records an early post before it
--   is sent, so a record that cannot be kept never posts it twice again.
ALTER TABLE hazem.chatwoot_inbound DROP CONSTRAINT chatwoot_inbound_wa_id_check;
ALTER TABLE hazem.chatwoot_inbound ADD CONSTRAINT chatwoot_inbound_wa_id_check
  CHECK (wa_id ~ '^([0-9]{8,15}|[A-Z]{2}\.[A-Za-z0-9.]{1,140})$');

-- migrate:down
DELETE FROM hazem.chatwoot_inbound WHERE wa_id !~ '^[0-9]{8,15}$';
ALTER TABLE hazem.chatwoot_inbound DROP CONSTRAINT chatwoot_inbound_wa_id_check;
ALTER TABLE hazem.chatwoot_inbound ADD CONSTRAINT chatwoot_inbound_wa_id_check CHECK (wa_id ~ '^[0-9]{8,15}$');
