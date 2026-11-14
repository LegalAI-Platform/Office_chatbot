-- v1.110 (the owner, 2026-11-14: «السيرش بار هنا حاسه بيسرش غلط» … «اعمل بحث شامل»). The panel's search compared a name as
-- typed, so «احمد» missed «أحمد», «فاطمه» missed «فاطمة» and «مصطفي» missed «مصطفى». hazem.search_text is the one form both
-- sides are compared in: lower case, no tashkeel and no tatweel, one alef (أ إ آ ٱ → ا), ه for ة, ي for ى and ئ, و for ؤ,
-- and the Persian ی and ک as ي and ك (WhatsApp names). The panel's search bar and the tasks page's search go through it.

-- migrate:up
CREATE FUNCTION hazem.search_text(p text) RETURNS text
LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $$
  -- U+064B–U+065F (the harakat), U+0670 (the dagger alef) and U+0640 (the tatweel) dropped; then, by code point so no
  -- look-alike letter slips in: أ إ آ ٱ → ا, ة → ه, ى ئ → ي, ؤ → و, ی (U+06CC) → ي, ک (U+06A9) → ك
  SELECT translate(regexp_replace(lower(p), '[' || chr(1611) || '-' || chr(1631) || chr(1648) || chr(1600) || ']', '', 'g'),
                   chr(1571) || chr(1573) || chr(1570) || chr(1649) || chr(1577) || chr(1609) || chr(1574) || chr(1572) || chr(1740) || chr(1705),
                   chr(1575) || chr(1575) || chr(1575) || chr(1575) || chr(1607) || chr(1610) || chr(1610) || chr(1608) || chr(1610) || chr(1603))
$$;
GRANT EXECUTE ON FUNCTION hazem.search_text(text) TO office_api, office_readonly;

-- migrate:down
DROP FUNCTION hazem.search_text(text);
