-- 0040_panel_avatar: each panel account can set its own photo (the owner: «أنا ممكن أحط صورتي عادي»).
--   hazem.panel_users  + avatar: the photo itself, kept in the database (no storage bucket: the panel
--                        makes it a 256×256 JPEG before upload, so it is a few dozen KB; 300 KB at most),
--                        its type (JPEG, PNG or WebP only, the bytes checked by the API) and when it was
--                        set (the panel's cache key). All three set together, or none.
-- office_api already has every column of the table (0009). office_readonly sees whether there is a photo
-- and when, never the photo.

-- migrate:up
ALTER TABLE hazem.panel_users
  ADD COLUMN avatar      bytea CHECK (avatar IS NULL OR octet_length(avatar) BETWEEN 1 AND 307200),
  ADD COLUMN avatar_type text CHECK (avatar_type IS NULL OR avatar_type IN ('image/jpeg', 'image/png', 'image/webp')),
  ADD COLUMN avatar_at   timestamptz,
  ADD CONSTRAINT panel_users_avatar_whole
    CHECK ((avatar IS NULL) = (avatar_type IS NULL) AND (avatar IS NULL) = (avatar_at IS NULL));

GRANT SELECT (avatar_type, avatar_at) ON hazem.panel_users TO office_readonly;

-- migrate:down
ALTER TABLE hazem.panel_users
  DROP CONSTRAINT panel_users_avatar_whole,
  DROP COLUMN avatar_at,
  DROP COLUMN avatar_type,
  DROP COLUMN avatar;
