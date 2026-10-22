-- 0007_approve_proposed_texts: the owner approved the 23 proposed texts of TASK 04
-- (docs/approved-texts-proposal.md) as they are, on 2026-10-22, in chat. From now on Hazem sends
-- them instead of their stand-ins. None of them promises a contact time (B-06).

-- migrate:up
UPDATE hazem.approved_texts
   SET active = true, updated_by = 'owner approval 2026-10-22'
 WHERE version = 1 AND source LIKE 'proposal TASK 04%' AND NOT active;

-- migrate:down
UPDATE hazem.approved_texts
   SET active = false, updated_by = 'migration 0007 down'
 WHERE version = 1 AND source LIKE 'proposal TASK 04%';
