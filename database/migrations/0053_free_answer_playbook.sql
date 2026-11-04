-- 0053_free_answer_playbook: DECISIONS v1.53 (phases 2 and 3). The owner (2026-11-04), after phase 1 worked: «تمام كمل».
-- With hazem.understanding_v2 on: what no approved text answers is said in the writer's words from the office facts and
-- «دليل التعامل» (free_answer, its approved words when the writer is off or doubtful), and «دليل التعامل» itself
-- (office_playbook), never sent, edited in «النصوص».

-- migrate:up
INSERT INTO hazem.approved_texts (key, version, text, max_chars, active, source, updated_by) VALUES
  ('free_answer', 1, 'معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏', 300, true, 'DECISIONS v1.53', 'owner approval 2026-11-04 (v1.53)'),
  ('office_playbook', 1, '- العميل اللي بيسأل بس أو مش عايز يكمّل: جاوبه على اللي سأله، وما تضغطش عليه، وسيب الباب مفتوح.
- العميل اللي شايف السعر غالي: اتفهّم الأول، وقول إن الاستشارة مع المستشار سعد فتحي سعد نفسه، وإن تمنها بيتحسب من أتعاب القضية لو الموضوع وصل لقضية، وإن الاستشارة بالتليفون أو الفيديو أقل. السعر ثابت، وحازم عمره ما بيوافق على تخفيض أو تقسيط من عنده: ده قرار المكتب.
- السرية: كل كلام العميل مع المكتب سري، والمكتب عمره ما بيفشي أسرار عملائه.
- العميل الخايف أو المتضايق: طمّنه بكلمة، وقوله إن المستشار هو اللي هيسمعه ويقوله يعمل إيه.', 600, true, 'DECISIONS v1.53', 'owner approval 2026-11-04 (v1.53)'),
  ('office_playbook_more', 1, '- لو قارن بمحامي تاني: ما تتكلمش عن حد تاني أبدًا، اتكلم عن المكتب بس.
- رقم المستشار الشخصي ما بيتديش: التواصل من هنا، أو في الاستشارة.
- أي سؤال عن القانون أو حقوقه أو فرصته في القضية: المستشار هو اللي بيجاوبه في الاستشارة، من غير أي معلومة قانونية.
- أي حاجة مش في حقايق المكتب: قول إن المستشار هيوضحها في الاستشارة، من غير ما تحدد وقت ولا توعد بحاجة.
- رد واحد قصير ومحترم، من غير ضغط ولا تكرار لنفس الكلام.', 600, true, 'DECISIONS v1.53', 'owner approval 2026-11-04 (v1.53)')
ON CONFLICT (key, version) DO NOTHING;

-- migrate:down
DELETE FROM hazem.approved_texts t WHERE t.updated_by = 'owner approval 2026-11-04 (v1.53)'
  AND NOT EXISTS (SELECT 1 FROM hazem.turn_replies r WHERE r.text_key = t.key AND r.text_version = t.version);
UPDATE hazem.approved_texts SET active = false WHERE updated_by = 'owner approval 2026-11-04 (v1.53)';
