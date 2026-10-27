-- 0022_urf_age_closed: DECISIONS v1.20 (B-11.1). After an Urfi marriage is refused for the age (rejected_age), a message
-- about the same marriage in the next 24 hours («ليه», a legal question, the request again, an offer or a price for
-- it) gets this one closing text, without the consultation offer, a price or buttons, twice at most, then silence.
-- Another matter goes on as usual. The owner chose wording (ب) on 2026-10-27 (docs/proposal-urf-age-refusal.md):
-- the office's policy, no legal statement, so B-20 needs no exception. Approved, so active at once.

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('urf_age_closed', 1, 'نعتذر لحضرتك، دي سياسة المكتب: مفيش عقود جواز لحد أقل من 18 سنة. لو محتاج حضرتك أي خدمة تانية، أنا موجود.',
   300, true, 'DECISIONS v1.20 B-11.1', 'migration 0022')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts WHERE key = 'urf_age_closed';
